using System.Text.Json;
using DumpAnalysisService.Hubs;
using DumpAnalysisService.Services;
using DumpAnalysisService.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Moq;

namespace DumpAnalysisService.Tests.Tasks;

#pragma warning disable MCPEXP001 // IMcpTaskStore / McpTask are experimental in SDK 1.3

public class JobManagerBackedTaskStoreTests
{
    private static (JobManagerBackedTaskStore Store, JobManagerService Jobs) CreateSut()
    {
        var clientProxy = new Mock<IClientProxy>();
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxy.Object);
        var hubContext = new Mock<IHubContext<ProgressHub>>();
        hubContext.Setup(h => h.Clients).Returns(clients.Object);

        var jobs = new JobManagerService(
            NullLogger<JobManagerService>.Instance,
            hubContext.Object);

        var store = new JobManagerBackedTaskStore(
            NullLogger<JobManagerBackedTaskStore>.Instance,
            jobs);

        return (store, jobs);
    }

    private static JsonRpcRequest CreateRequest() =>
        new()
        {
            Method = "tools/call",
            Id = new RequestId("req-1"),
        };

    private static McpTaskMetadata CreateMetadata(TimeSpan? ttl = null) =>
        new() { TimeToLive = ttl };

    [Fact]
    public async Task CreateTaskAsync_ValidRequest_ReturnsWorkingTask()
    {
        var (store, _) = CreateSut();

        var task = await store.CreateTaskAsync(
            CreateMetadata(TimeSpan.FromMinutes(10)),
            new RequestId("req-1"),
            CreateRequest(),
            sessionId: "session-A",
            CancellationToken.None);

        Assert.NotNull(task);
        Assert.False(string.IsNullOrEmpty(task.TaskId));
        Assert.Equal(McpTaskStatus.Working, task.Status);
        Assert.NotEqual(default, task.CreatedAt);
        Assert.NotEqual(default, task.LastUpdatedAt);
        Assert.Equal(TimeSpan.FromMinutes(10), task.TimeToLive);
    }

    [Fact]
    public async Task GetTaskAsync_AfterCreate_ReturnsSameTask()
    {
        var (store, _) = CreateSut();

        var created = await store.CreateTaskAsync(
            CreateMetadata(),
            new RequestId("req-1"),
            CreateRequest(),
            sessionId: "session-A",
            CancellationToken.None);

        var fetched = await store.GetTaskAsync(created.TaskId, "session-A", CancellationToken.None);

        Assert.NotNull(fetched);
        Assert.Equal(created.TaskId, fetched!.TaskId);
        Assert.Equal(McpTaskStatus.Working, fetched.Status);
    }

    [Fact]
    public async Task GetTaskAsync_UnknownId_ReturnsNull()
    {
        var (store, _) = CreateSut();

        var fetched = await store.GetTaskAsync("does-not-exist", sessionId: null, CancellationToken.None);

        Assert.Null(fetched);
    }

    [Fact]
    public async Task GetTaskAsync_WrongSessionId_ReturnsNull()
    {
        var (store, _) = CreateSut();

        var created = await store.CreateTaskAsync(
            CreateMetadata(),
            new RequestId("req-1"),
            CreateRequest(),
            sessionId: "session-A",
            CancellationToken.None);

        var fetched = await store.GetTaskAsync(created.TaskId, "session-B", CancellationToken.None);

        Assert.Null(fetched);
    }

    [Fact]
    public async Task CompletingUnderlyingJob_MakesGetTaskReturnCompleted()
    {
        var (store, jobs) = CreateSut();

        var created = await store.CreateTaskAsync(
            CreateMetadata(),
            new RequestId("req-1"),
            CreateRequest(),
            sessionId: null,
            CancellationToken.None);

        // TaskId is the underlying jobId in this adapter
        await jobs.CompleteJobAsync(created.TaskId, "result-payload");

        var fetched = await store.GetTaskAsync(created.TaskId, sessionId: null, CancellationToken.None);

        Assert.NotNull(fetched);
        Assert.Equal(McpTaskStatus.Completed, fetched!.Status);
    }

    [Fact]
    public async Task FailingUnderlyingJob_MakesGetTaskReturnFailedWithMessage()
    {
        var (store, jobs) = CreateSut();

        var created = await store.CreateTaskAsync(
            CreateMetadata(),
            new RequestId("req-1"),
            CreateRequest(),
            sessionId: null,
            CancellationToken.None);

        await jobs.FailJobAsync(created.TaskId, "boom: simulated failure");

        var fetched = await store.GetTaskAsync(created.TaskId, sessionId: null, CancellationToken.None);

        Assert.NotNull(fetched);
        Assert.Equal(McpTaskStatus.Failed, fetched!.Status);
        Assert.Contains("boom", fetched.StatusMessage ?? string.Empty);
    }

    [Fact]
    public async Task CancelTaskAsync_CancelsUnderlyingJob()
    {
        var (store, jobs) = CreateSut();

        var created = await store.CreateTaskAsync(
            CreateMetadata(),
            new RequestId("req-1"),
            CreateRequest(),
            sessionId: null,
            CancellationToken.None);

        var cancelled = await store.CancelTaskAsync(created.TaskId, sessionId: null, CancellationToken.None);

        Assert.Equal(McpTaskStatus.Cancelled, cancelled.Status);

        // Verify the underlying job was cancelled.
        var jobStatus = jobs.GetJobStatus(created.TaskId);
        Assert.Equal(Shared.Models.JobState.Cancelled, jobStatus.State);
    }

    [Fact]
    public async Task CancelTaskAsync_AlreadyCancelled_IsIdempotent()
    {
        var (store, _) = CreateSut();

        var created = await store.CreateTaskAsync(
            CreateMetadata(),
            new RequestId("req-1"),
            CreateRequest(),
            sessionId: null,
            CancellationToken.None);

        await store.CancelTaskAsync(created.TaskId, sessionId: null, CancellationToken.None);

        // Second cancel must not throw.
        var second = await store.CancelTaskAsync(created.TaskId, sessionId: null, CancellationToken.None);

        Assert.Equal(McpTaskStatus.Cancelled, second.Status);
    }

    [Fact]
    public async Task StoreTaskResultAsync_PersistsResultForRetrieval()
    {
        var (store, _) = CreateSut();

        var created = await store.CreateTaskAsync(
            CreateMetadata(),
            new RequestId("req-1"),
            CreateRequest(),
            sessionId: "session-A",
            CancellationToken.None);

        var resultJson = JsonSerializer.SerializeToElement(new { value = 42, name = "answer" });

        var stored = await store.StoreTaskResultAsync(
            created.TaskId,
            McpTaskStatus.Completed,
            resultJson,
            sessionId: "session-A",
            CancellationToken.None);

        Assert.Equal(McpTaskStatus.Completed, stored.Status);

        var retrieved = await store.GetTaskResultAsync(created.TaskId, "session-A", CancellationToken.None);
        Assert.Equal(42, retrieved.GetProperty("value").GetInt32());
        Assert.Equal("answer", retrieved.GetProperty("name").GetString());
    }

    [Fact]
    public async Task StoreTaskResultAsync_AlreadyTerminal_ThrowsInvalidOperationException()
    {
        var (store, _) = CreateSut();

        var created = await store.CreateTaskAsync(
            CreateMetadata(),
            new RequestId("req-1"),
            CreateRequest(),
            sessionId: null,
            CancellationToken.None);

        var resultJson = JsonSerializer.SerializeToElement(new { ok = true });
        await store.StoreTaskResultAsync(
            created.TaskId,
            McpTaskStatus.Completed,
            resultJson,
            sessionId: null,
            CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.StoreTaskResultAsync(
                created.TaskId,
                McpTaskStatus.Completed,
                resultJson,
                sessionId: null,
                CancellationToken.None));
    }

    [Fact]
    public async Task ListTasksAsync_ReturnsAllCreatedTasks()
    {
        var (store, _) = CreateSut();

        var t1 = await store.CreateTaskAsync(CreateMetadata(), new RequestId("r1"), CreateRequest(), sessionId: null, CancellationToken.None);
        var t2 = await store.CreateTaskAsync(CreateMetadata(), new RequestId("r2"), CreateRequest(), sessionId: null, CancellationToken.None);
        var t3 = await store.CreateTaskAsync(CreateMetadata(), new RequestId("r3"), CreateRequest(), sessionId: null, CancellationToken.None);

        var list = await store.ListTasksAsync(cursor: null, sessionId: null, CancellationToken.None);

        Assert.NotNull(list);
        var ids = list.Tasks.Select(t => t.TaskId).ToHashSet();
        Assert.Contains(t1.TaskId, ids);
        Assert.Contains(t2.TaskId, ids);
        Assert.Contains(t3.TaskId, ids);
    }
}

#pragma warning restore MCPEXP001
