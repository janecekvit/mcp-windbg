using System.Net.Http.Json;
using DumpAnalysisService.IntegrationTests.Fixtures;
using Shared.Models;

namespace DumpAnalysisService.IntegrationTests.Tests;

[Collection(IntegrationCollection.Name)]
[Trait("Category", "Integration")]
public class SessionLifecycleTests
{
    private readonly ServiceFixture _service;
    private readonly DumpFixture _dump;

    public SessionLifecycleTests(ServiceFixture service, DumpFixture dump)
    {
        _service = service;
        _dump = dump;
    }

    [SkippableFact]
    public async Task ListJobs_AfterLoadAndClose_IncludesCompletedJobs()
    {
        Skip.IfNot(CdbAvailability.IsAvailable, CdbAvailability.SkipReason);

        // 1. load_dump
        var loadJobId = await StartJobAsync(
            "/api/jobs/load-dump",
            new LoadDumpRequest(_dump.DumpPath));
        var loadResult = await WaitForJobAsync(loadJobId, TimeSpan.FromMinutes(20));
        Assert.Equal(JobState.Completed, loadResult.State);
        Assert.False(string.IsNullOrWhiteSpace(loadResult.Result));
        var sessionId = loadResult.Result!;

        // 2. close_session
        var closeJobId = await StartJobAsync(
            "/api/jobs/close-session",
            new CloseSessionRequest(sessionId));
        var closeResult = await WaitForJobAsync(closeJobId, TimeSpan.FromMinutes(1));
        Assert.Equal(JobState.Completed, closeResult.State);

        // 3. GET /api/jobs - verify both jobs are listed
        var listResp = await _service.Client.GetAsync("/api/jobs");
        listResp.EnsureSuccessStatusCode();
        var jobs = await listResp.Content.ReadFromJsonAsync<JobStatus[]>();
        Assert.NotNull(jobs);

        Assert.Contains(jobs!, j => j.JobId == loadJobId);
        Assert.Contains(jobs!, j => j.JobId == closeJobId);
    }

    [SkippableFact]
    public async Task CancelJob_ReturnsSuccess()
    {
        Skip.IfNot(CdbAvailability.IsAvailable, CdbAvailability.SkipReason);

        // Start the slowest operation (load-dump) so we have something to cancel.
        var loadJobId = await StartJobAsync(
            "/api/jobs/load-dump",
            new LoadDumpRequest(_dump.DumpPath));

        // Immediately request cancellation.
        var cancelResp = await _service.Client.PostAsync(
            $"/api/jobs/{loadJobId}/cancel",
            content: null);
        Assert.True(
            cancelResp.IsSuccessStatusCode,
            $"Cancel request did not succeed. Status: {(int)cancelResp.StatusCode}");

        // Wait for the job to reach a terminal state. Cancellation may race with
        // completion when symbols are warm, so any terminal state is acceptable.
        var finalStatus = await WaitForJobAsync(loadJobId, TimeSpan.FromMinutes(2));
        Assert.Contains(
            finalStatus.State,
            new[] { JobState.Cancelled, JobState.Failed, JobState.Completed });

        // If the load actually completed, clean up the session to avoid leaks.
        if (finalStatus.State == JobState.Completed
            && !string.IsNullOrWhiteSpace(finalStatus.Result))
        {
            var closeJobId = await StartJobAsync(
                "/api/jobs/close-session",
                new CloseSessionRequest(finalStatus.Result!));
            await WaitForJobAsync(closeJobId, TimeSpan.FromMinutes(1));
        }
    }

    private async Task<string> StartJobAsync<TRequest>(string url, TRequest body)
    {
        var resp = await _service.Client.PostAsJsonAsync(url, body);
        resp.EnsureSuccessStatusCode();
        var created = await resp.Content.ReadFromJsonAsync<JobCreatedResponse>();
        Assert.NotNull(created);
        Assert.False(string.IsNullOrWhiteSpace(created!.JobId));
        return created.JobId;
    }

    private async Task<JobStatus> WaitForJobAsync(string jobId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var resp = await _service.Client.GetAsync($"/api/jobs/{jobId}");
            resp.EnsureSuccessStatusCode();
            var status = await resp.Content.ReadFromJsonAsync<JobStatus>();
            Assert.NotNull(status);
            if (status!.State is JobState.Completed
                              or JobState.Failed
                              or JobState.Cancelled)
                return status;
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
        throw new TimeoutException(
            $"Job {jobId} did not finish within {timeout}.");
    }
}
