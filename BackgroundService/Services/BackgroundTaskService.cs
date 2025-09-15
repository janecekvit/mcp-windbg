using System.Collections.Concurrent;
using Shared.Models;

namespace BackgroundService.Services;

public class BackgroundTaskService : IBackgroundTaskService
{
    private readonly ILogger<BackgroundTaskService> _logger;
    private readonly ISessionManagerService _sessionManager;
    private readonly ConcurrentDictionary<string, BackgroundTaskInfo> _tasks = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellationTokens = new();

    public BackgroundTaskService(ILogger<BackgroundTaskService> logger, ISessionManagerService sessionManager)
    {
        _logger = logger;
        _sessionManager = sessionManager;
    }

    public Task<string> StartLoadDumpAsync(string dumpFilePath)
    {
        var taskId = GenerateTaskId();
        var cts = new CancellationTokenSource();
        _cancellationTokens[taskId] = cts;

        var taskInfo = new BackgroundTaskInfo(
            taskId,
            BackgroundTaskType.LoadDump,
            $"Loading dump: {dumpFilePath}",
            BackgroundTaskStatus.Running,
            DateTime.UtcNow,
            null,
            null,
            null);

        _tasks[taskId] = taskInfo;

        _ = Task.Run(async () =>
        {
            var progressCts = new CancellationTokenSource();
            var progressTask = LogProgressPeriodically(taskId, "Loading dump file...", progressCts.Token);

            try
            {
                var sessionId = await _sessionManager.CreateSessionWithDumpAsync(dumpFilePath, cts.Token);

                _tasks[taskId] = taskInfo with
                {
                    Status = BackgroundTaskStatus.Completed,
                    CompletedAt = DateTime.UtcNow,
                    Result = $"Session created: {sessionId}",
                    SessionId = sessionId
                };

                _logger.LogInformation("Background load dump task {TaskId} completed successfully", taskId);
            }
            catch (OperationCanceledException)
            {
                _tasks[taskId] = taskInfo with
                {
                    Status = BackgroundTaskStatus.Cancelled,
                    CompletedAt = DateTime.UtcNow
                };
                _logger.LogInformation("Background load dump task {TaskId} was cancelled", taskId);
            }
            catch (Exception ex)
            {
                _tasks[taskId] = taskInfo with
                {
                    Status = BackgroundTaskStatus.Failed,
                    CompletedAt = DateTime.UtcNow,
                    Error = ex.Message
                };
                _logger.LogError(ex, "Background load dump task {TaskId} failed", taskId);
            }
            finally
            {
                // Stop progress logging
                progressCts.Cancel();
                _cancellationTokens.TryRemove(taskId, out _);
            }
        }, cts.Token);

        return Task.FromResult(taskId);
    }

    public Task<string> StartBasicAnalysisAsync(string sessionId)
    {
        var taskId = GenerateTaskId();
        var cts = new CancellationTokenSource();
        _cancellationTokens[taskId] = cts;

        var taskInfo = new BackgroundTaskInfo(
            taskId,
            BackgroundTaskType.BasicAnalysis,
            $"Running basic analysis on session: {sessionId}",
            BackgroundTaskStatus.Running,
            DateTime.UtcNow,
            null,
            null,
            sessionId);

        _tasks[taskId] = taskInfo;

        _ = Task.Run(async () =>
        {
            var progressCts = new CancellationTokenSource();
            var progressTask = LogProgressPeriodically(taskId, "Running basic analysis...", progressCts.Token);

            try
            {
                var result = await _sessionManager.ExecuteBasicAnalysisAsync(sessionId, cts.Token);

                _tasks[taskId] = taskInfo with
                {
                    Status = BackgroundTaskStatus.Completed,
                    CompletedAt = DateTime.UtcNow,
                    Result = result
                };

                _logger.LogInformation("Background basic analysis task {TaskId} completed successfully", taskId);
            }
            catch (OperationCanceledException)
            {
                _tasks[taskId] = taskInfo with
                {
                    Status = BackgroundTaskStatus.Cancelled,
                    CompletedAt = DateTime.UtcNow
                };
                _logger.LogInformation("Background basic analysis task {TaskId} was cancelled", taskId);
            }
            catch (Exception ex)
            {
                _tasks[taskId] = taskInfo with
                {
                    Status = BackgroundTaskStatus.Failed,
                    CompletedAt = DateTime.UtcNow,
                    Error = ex.Message
                };
                _logger.LogError(ex, "Background basic analysis task {TaskId} failed", taskId);
            }
            finally
            {
                // Stop progress logging
                progressCts.Cancel();
                _cancellationTokens.TryRemove(taskId, out _);
            }
        }, cts.Token);

        return Task.FromResult(taskId);
    }

    public Task<string> StartPredefinedAnalysisAsync(string sessionId, string analysisType)
    {
        var taskId = GenerateTaskId();
        var cts = new CancellationTokenSource();
        _cancellationTokens[taskId] = cts;

        var taskInfo = new BackgroundTaskInfo(
            taskId,
            BackgroundTaskType.PredefinedAnalysis,
            $"Running {analysisType} analysis on session: {sessionId}",
            BackgroundTaskStatus.Running,
            DateTime.UtcNow,
            null,
            null,
            sessionId);

        _tasks[taskId] = taskInfo;

        _ = Task.Run(async () =>
        {
            var progressCts = new CancellationTokenSource();
            var progressTask = LogProgressPeriodically(taskId, $"Running {analysisType} analysis...", progressCts.Token);

            try
            {
                var result = await _sessionManager.ExecutePredefinedAnalysisAsync(sessionId, analysisType, cts.Token);

                _tasks[taskId] = taskInfo with
                {
                    Status = BackgroundTaskStatus.Completed,
                    CompletedAt = DateTime.UtcNow,
                    Result = result
                };

                _logger.LogInformation("Background predefined analysis task {TaskId} completed successfully", taskId);
            }
            catch (OperationCanceledException)
            {
                _tasks[taskId] = taskInfo with
                {
                    Status = BackgroundTaskStatus.Cancelled,
                    CompletedAt = DateTime.UtcNow
                };
                _logger.LogInformation("Background predefined analysis task {TaskId} was cancelled", taskId);
            }
            catch (Exception ex)
            {
                _tasks[taskId] = taskInfo with
                {
                    Status = BackgroundTaskStatus.Failed,
                    CompletedAt = DateTime.UtcNow,
                    Error = ex.Message
                };
                _logger.LogError(ex, "Background predefined analysis task {TaskId} failed", taskId);
            }
            finally
            {
                // Stop progress logging
                progressCts.Cancel();
                _cancellationTokens.TryRemove(taskId, out _);
            }
        }, cts.Token);

        return Task.FromResult(taskId);
    }

    public Task<string> StartExecuteCommandAsync(string sessionId, string command)
    {
        var taskId = GenerateTaskId();
        var cts = new CancellationTokenSource();
        _cancellationTokens[taskId] = cts;

        var taskInfo = new BackgroundTaskInfo(
            taskId,
            BackgroundTaskType.ExecuteCommand,
            $"Executing command '{command}' on session: {sessionId}",
            BackgroundTaskStatus.Running,
            DateTime.UtcNow,
            null,
            null,
            sessionId);

        _tasks[taskId] = taskInfo;

        _ = Task.Run(async () =>
        {
            var progressCts = new CancellationTokenSource();
            var progressTask = LogProgressPeriodically(taskId, $"Executing command: {command}", progressCts.Token);

            try
            {
                var result = await _sessionManager.ExecuteCommandAsync(sessionId, command, cts.Token);

                _tasks[taskId] = taskInfo with
                {
                    Status = BackgroundTaskStatus.Completed,
                    CompletedAt = DateTime.UtcNow,
                    Result = result
                };

                _logger.LogInformation("Background execute command task {TaskId} completed successfully", taskId);
            }
            catch (OperationCanceledException)
            {
                _tasks[taskId] = taskInfo with
                {
                    Status = BackgroundTaskStatus.Cancelled,
                    CompletedAt = DateTime.UtcNow
                };
                _logger.LogInformation("Background execute command task {TaskId} was cancelled", taskId);
            }
            catch (Exception ex)
            {
                _tasks[taskId] = taskInfo with
                {
                    Status = BackgroundTaskStatus.Failed,
                    CompletedAt = DateTime.UtcNow,
                    Error = ex.Message
                };
                _logger.LogError(ex, "Background execute command task {TaskId} failed", taskId);
            }
            finally
            {
                // Stop progress logging
                progressCts.Cancel();
                _cancellationTokens.TryRemove(taskId, out _);
            }
        }, cts.Token);

        return Task.FromResult(taskId);
    }

    public Task<BackgroundTaskStatus> GetTaskStatusAsync(string taskId)
    {
        if (_tasks.TryGetValue(taskId, out var task))
        {
            return Task.FromResult(task.Status);
        }
        throw new ArgumentException($"Task {taskId} not found");
    }

    public Task CancelTaskAsync(string taskId)
    {
        if (_cancellationTokens.TryGetValue(taskId, out var cts))
        {
            cts.Cancel();
            _logger.LogInformation("Cancelled background task {TaskId}", taskId);
        }
        return Task.CompletedTask;
    }

    public IReadOnlyList<BackgroundTaskInfo> GetAllTasks()
    {
        return _tasks.Values.ToList();
    }

    private static string GenerateTaskId()
    {
        return Guid.NewGuid().ToString("N")[..8];
    }

    private Task LogProgressPeriodically(string taskId, string message, CancellationToken cancellationToken)
    {
        var progressTask = Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                    _logger.LogInformation("Background task {TaskId} still running: {Message}", taskId, message);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, cancellationToken);

        // Don't await this task - it should run in parallel
        return progressTask;
    }
}