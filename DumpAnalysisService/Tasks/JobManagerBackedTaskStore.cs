using System.Collections.Concurrent;
using System.Text.Json;
using DumpAnalysisService.Services;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Shared.Models;

namespace DumpAnalysisService.Tasks;

#pragma warning disable MCPEXP001 // IMcpTaskStore / McpTask are experimental MCP Tasks APIs in SDK 1.3

/// <summary>
/// Adapter that exposes the experimental MCP <see cref="IMcpTaskStore"/> surface on top of the
/// existing <see cref="IJobManagerService"/>. One MCP TaskId is mapped 1:1 to one JobId.
/// </summary>
public sealed class JobManagerBackedTaskStore : IMcpTaskStore
{
    private readonly ILogger<JobManagerBackedTaskStore> _logger;
    private readonly IJobManagerService _jobs;
    private readonly ConcurrentDictionary<string, TaskEntry> _entries = new();

    public JobManagerBackedTaskStore(
        ILogger<JobManagerBackedTaskStore> logger,
        IJobManagerService jobs)
    {
        _logger = logger;
        _jobs = jobs;
    }

    public Task<McpTask> CreateTaskAsync(
        McpTaskMetadata taskParams,
        RequestId requestId,
        JsonRpcRequest request,
        string? sessionId,
        CancellationToken cancellationToken)
    {
        // We create an underlying job to carry the work. Operation type is a generic placeholder —
        // the SDK only cares about lifecycle. Concrete operations are created by DebuggerTools.
        var jobId = _jobs.CreateJob(JobOperationType.ExecuteCommand, sessionId);

        var now = DateTimeOffset.UtcNow;
        var entry = new TaskEntry
        {
            TaskId = jobId,
            SessionId = sessionId,
            CreatedAt = now,
            LastUpdatedAt = now,
            TimeToLive = taskParams.TimeToLive,
            StatusOverride = null,
            StatusMessageOverride = null,
            Result = null,
        };

        _entries[jobId] = entry;

        _logger.LogInformation(
            "Created MCP task {TaskId} for session {SessionId} (requestId={RequestId})",
            jobId, sessionId ?? "(none)", requestId);

        return Task.FromResult(_BuildMcpTask(entry));
    }

    public Task<McpTask?> GetTaskAsync(
        string taskId,
        string? sessionId,
        CancellationToken cancellationToken)
    {
        if (!_entries.TryGetValue(taskId, out var entry))
            return Task.FromResult<McpTask?>(null);

        if (!_SessionMatches(entry, sessionId))
            return Task.FromResult<McpTask?>(null);

        return Task.FromResult<McpTask?>(_BuildMcpTask(entry));
    }

    public async Task<McpTask> StoreTaskResultAsync(
        string taskId,
        McpTaskStatus status,
        JsonElement result,
        string? sessionId,
        CancellationToken cancellationToken)
    {
        if (!_entries.TryGetValue(taskId, out var entry))
            throw new InvalidOperationException($"Task '{taskId}' not found.");

        if (!_SessionMatches(entry, sessionId))
            throw new InvalidOperationException($"Task '{taskId}' is not accessible from the requesting session.");

        var current = _ResolveStatus(entry);
        if (_IsTerminal(current))
            throw new InvalidOperationException(
                $"Task '{taskId}' is already in terminal state {current}; cannot overwrite result.");

        lock (entry)
        {
            entry.StatusOverride = status;
            entry.Result = result;
            entry.LastUpdatedAt = DateTimeOffset.UtcNow;
        }

        // Reflect terminal status into the underlying job so other observers see consistent state.
        var resultText = result.ValueKind == JsonValueKind.Undefined ? string.Empty : result.GetRawText();
        switch (status)
        {
            case McpTaskStatus.Completed:
                await _jobs.CompleteJobAsync(taskId, resultText);
                break;
            case McpTaskStatus.Failed:
                await _jobs.FailJobAsync(taskId, resultText);
                break;
            case McpTaskStatus.Cancelled:
                await _jobs.CancelJobAsync(taskId);
                break;
            default:
                // Working / InputRequired are not terminal; SDK contract expects terminal here but be permissive.
                break;
        }

        return _BuildMcpTask(entry);
    }

    public Task<JsonElement> GetTaskResultAsync(
        string taskId,
        string? sessionId,
        CancellationToken cancellationToken)
    {
        if (!_entries.TryGetValue(taskId, out var entry))
            throw new InvalidOperationException($"Task '{taskId}' not found.");

        if (!_SessionMatches(entry, sessionId))
            throw new InvalidOperationException($"Task '{taskId}' is not accessible from the requesting session.");

        if (entry.Result is { } cached)
            return Task.FromResult(cached);

        // Fall back to underlying job result/error text wrapped as a JsonElement.
        var status = _jobs.GetJobStatus(taskId);
        var payload = status.Result ?? status.Error ?? string.Empty;
        var element = JsonSerializer.SerializeToElement(payload);
        return Task.FromResult(element);
    }

    public Task<McpTask> UpdateTaskStatusAsync(
        string taskId,
        McpTaskStatus status,
        string? statusMessage,
        string? sessionId,
        CancellationToken cancellationToken)
    {
        if (!_entries.TryGetValue(taskId, out var entry))
            throw new InvalidOperationException($"Task '{taskId}' not found.");

        if (!_SessionMatches(entry, sessionId))
            throw new InvalidOperationException($"Task '{taskId}' is not accessible from the requesting session.");

        lock (entry)
        {
            entry.StatusOverride = status;
            entry.StatusMessageOverride = statusMessage;
            entry.LastUpdatedAt = DateTimeOffset.UtcNow;
        }

        return Task.FromResult(_BuildMcpTask(entry));
    }

    public Task<ListTasksResult> ListTasksAsync(
        string? cursor,
        string? sessionId,
        CancellationToken cancellationToken)
    {
        var filtered = _entries.Values
            .Where(e => _SessionMatches(e, sessionId))
            .OrderByDescending(e => e.CreatedAt)
            .Select(_BuildMcpTask)
            .ToList();

        var result = new ListTasksResult
        {
            Tasks = filtered,
            NextCursor = null,
        };

        return Task.FromResult(result);
    }

    public async Task<McpTask> CancelTaskAsync(
        string taskId,
        string? sessionId,
        CancellationToken cancellationToken)
    {
        if (!_entries.TryGetValue(taskId, out var entry))
            throw new InvalidOperationException($"Task '{taskId}' not found.");

        if (!_SessionMatches(entry, sessionId))
            throw new InvalidOperationException($"Task '{taskId}' is not accessible from the requesting session.");

        var current = _ResolveStatus(entry);
        if (_IsTerminal(current))
        {
            // Idempotent: return existing task unchanged.
            return _BuildMcpTask(entry);
        }

        lock (entry)
        {
            entry.StatusOverride = McpTaskStatus.Cancelled;
            entry.StatusMessageOverride = "Cancelled";
            entry.LastUpdatedAt = DateTimeOffset.UtcNow;
        }

        await _jobs.CancelJobAsync(taskId);

        return _BuildMcpTask(entry);
    }

    private McpTask _BuildMcpTask(TaskEntry entry)
    {
        var status = _ResolveStatus(entry);
        var message = _ResolveStatusMessage(entry, status);

        return new McpTask
        {
            TaskId = entry.TaskId,
            Status = status,
            StatusMessage = message,
            CreatedAt = entry.CreatedAt,
            LastUpdatedAt = entry.LastUpdatedAt,
            TimeToLive = entry.TimeToLive,
        };
    }

    private McpTaskStatus _ResolveStatus(TaskEntry entry)
    {
        if (entry.StatusOverride is { } overridden)
            return overridden;

        JobStatus jobStatus;
        try
        {
            jobStatus = _jobs.GetJobStatus(entry.TaskId);
        }
        catch (ArgumentException)
        {
            // Underlying job has been cleaned up — assume terminal cancellation.
            return McpTaskStatus.Cancelled;
        }

        return jobStatus.State switch
        {
            JobState.Queued => McpTaskStatus.Working,
            JobState.Running => McpTaskStatus.Working,
            JobState.Completed => McpTaskStatus.Completed,
            JobState.Failed => McpTaskStatus.Failed,
            JobState.Cancelled => McpTaskStatus.Cancelled,
            _ => McpTaskStatus.Working,
        };
    }

    private string? _ResolveStatusMessage(TaskEntry entry, McpTaskStatus status)
    {
        if (!string.IsNullOrEmpty(entry.StatusMessageOverride))
            return entry.StatusMessageOverride;

        try
        {
            var jobStatus = _jobs.GetJobStatus(entry.TaskId);
            return status switch
            {
                McpTaskStatus.Failed => jobStatus.Error ?? jobStatus.Message,
                McpTaskStatus.Cancelled => jobStatus.Error ?? jobStatus.Message,
                _ => jobStatus.Message,
            };
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool _SessionMatches(TaskEntry entry, string? requestSessionId)
    {
        // If the caller does not specify a session, allow access (single-process, no isolation).
        if (requestSessionId is null)
            return true;

        // If the entry has no owning session, accept any caller.
        if (entry.SessionId is null)
            return true;

        return string.Equals(entry.SessionId, requestSessionId, StringComparison.Ordinal);
    }

    private static bool _IsTerminal(McpTaskStatus status) =>
        status is McpTaskStatus.Completed or McpTaskStatus.Failed or McpTaskStatus.Cancelled;

    private sealed class TaskEntry
    {
        public required string TaskId { get; init; }
        public string? SessionId { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset LastUpdatedAt { get; set; }
        public TimeSpan? TimeToLive { get; init; }

        /// <summary>
        /// If set, takes precedence over the status derived from the underlying job.
        /// </summary>
        public McpTaskStatus? StatusOverride { get; set; }

        public string? StatusMessageOverride { get; set; }

        public JsonElement? Result { get; set; }
    }
}

#pragma warning restore MCPEXP001
