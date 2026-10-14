using DumpAnalysisService.Hubs;
using DumpAnalysisService.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Models;

namespace DumpAnalysisService.Tests;

public sealed class JobManagerServiceTests : IDisposable
{
    private readonly JobManagerService _jobManager;

    public JobManagerServiceTests()
    {
        var clientProxy = new Mock<IClientProxy>();
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxy.Object);
        var hubContext = new Mock<IHubContext<ProgressHub>>();
        hubContext.Setup(h => h.Clients).Returns(clients.Object);

        _jobManager = new JobManagerService(NullLogger<JobManagerService>.Instance, hubContext.Object);
    }

    public void Dispose() => _jobManager.Dispose();

    [Fact]
    public async Task UpdateProgressAsync_AfterCompletion_DoesNotChangeState()
    {
        var jobId = _jobManager.CreateJob(JobOperationType.ExecuteCommand);
        await _jobManager.CompleteJobAsync(jobId, "done");

        await _jobManager.UpdateProgressAsync(jobId, JobPhase.ExecutingCommand, 0.3, "late progress");

        var status = _jobManager.GetJobStatus(jobId);
        Assert.Equal(JobState.Completed, status.State);
        Assert.Equal(1.0, status.Progress);
        Assert.Equal("done", status.Result);
    }

    [Fact]
    public async Task CompleteJobAsync_AfterCancel_KeepsCancelled()
    {
        var jobId = _jobManager.CreateJob(JobOperationType.LoadDump);
        await _jobManager.CancelJobAsync(jobId);

        await _jobManager.CompleteJobAsync(jobId, "session-id");

        var status = _jobManager.GetJobStatus(jobId);
        Assert.Equal(JobState.Cancelled, status.State);
        Assert.Null(status.Result);
    }

    [Fact]
    public async Task FailJobAsync_AfterCancel_KeepsCancelled()
    {
        var jobId = _jobManager.CreateJob(JobOperationType.LoadDump);
        await _jobManager.CancelJobAsync(jobId);

        await _jobManager.FailJobAsync(jobId, "process killed");

        Assert.Equal(JobState.Cancelled, _jobManager.GetJobStatus(jobId).State);
    }

    [Fact]
    public async Task UpdateProgressAsync_QueuedJob_BecomesRunning()
    {
        var jobId = _jobManager.CreateJob(JobOperationType.LoadDump);

        await _jobManager.UpdateProgressAsync(jobId, JobPhase.LoadingDump, 0.5, "loading");

        var status = _jobManager.GetJobStatus(jobId);
        Assert.Equal(JobState.Running, status.State);
        Assert.Equal(0.5, status.Progress);
        Assert.NotNull(status.StartedAt);
    }
}
