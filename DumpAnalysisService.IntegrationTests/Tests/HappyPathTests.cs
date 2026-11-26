using System.Net.Http.Json;
using DumpAnalysisService.IntegrationTests.Fixtures;
using Shared.Models;

namespace DumpAnalysisService.IntegrationTests.Tests;

[Collection(IntegrationCollection.Name)]
[Trait("Category", "Integration")]
public class HappyPathTests
{
    private readonly ServiceFixture _service;
    private readonly DumpFixture _dump;

    public HappyPathTests(ServiceFixture service, DumpFixture dump)
    {
        _service = service;
        _dump = dump;
    }

    [SkippableFact]
    public async Task LoadDump_BasicAnalysis_CloseSession_HappyPath()
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

        // 2. basic_analysis
        var analysisJobId = await StartJobAsync(
            "/api/jobs/basic-analysis",
            new BasicAnalysisRequest(sessionId));
        var analysisResult = await WaitForJobAsync(
            analysisJobId, TimeSpan.FromMinutes(10));
        Assert.Equal(JobState.Completed, analysisResult.State);

        var output = analysisResult.Result ?? "";
        Assert.Contains("ANALYZE",
            output,
            StringComparison.OrdinalIgnoreCase);

        // 3. close_session
        var closeJobId = await StartJobAsync(
            "/api/jobs/close-session",
            new CloseSessionRequest(sessionId));
        var closeResult = await WaitForJobAsync(closeJobId, TimeSpan.FromMinutes(1));
        Assert.Equal(JobState.Completed, closeResult.State);
    }

    [SkippableFact]
    public async Task LoadDump_NonAsciiPath_CommandOutputKeepsTheCharacters()
    {
        Skip.IfNot(CdbAvailability.IsAvailable, CdbAvailability.SkipReason);

        // cdb.exe writes the ANSI code page on its pipes; "š" exists in 1250 and 1252.
        var directory = Path.Combine(Path.GetTempPath(), "mcp-windbg-tests", $"dumps-š-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var dumpPath = Path.Combine(directory, "snapshot-š.dmp");
        File.Copy(_dump.DumpPath, dumpPath);
        try
        {
            var loadResult = await WaitForJobAsync(
                await StartJobAsync("/api/jobs/load-dump", new LoadDumpRequest(dumpPath)), TimeSpan.FromMinutes(20));
            Assert.Equal(JobState.Completed, loadResult.State);
            var sessionId = loadResult.Result!;

            var commandResult = await WaitForJobAsync(
                await StartJobAsync("/api/jobs/execute-command", new ExecuteCommandRequest(sessionId, "||")), TimeSpan.FromMinutes(2));
            Assert.Equal(JobState.Completed, commandResult.State);
            Assert.Contains("snapshot-š.dmp", commandResult.Result, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain('\uFFFD', commandResult.Result!);

            await WaitForJobAsync(await StartJobAsync("/api/jobs/close-session", new CloseSessionRequest(sessionId)), TimeSpan.FromMinutes(1));
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // cdb.exe may still hold the dump open for a moment; the temp directory is cleaned up later.
            }
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
