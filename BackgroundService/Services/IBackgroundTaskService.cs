using Shared.Models;

namespace BackgroundService.Services;

public interface IBackgroundTaskService
{
    Task<string> StartLoadDumpAsync(string dumpFilePath);
    Task<string> StartBasicAnalysisAsync(string sessionId);
    Task<string> StartPredefinedAnalysisAsync(string sessionId, string analysisType);
    Task<string> StartExecuteCommandAsync(string sessionId, string command);
    Task<BackgroundTaskStatus> GetTaskStatusAsync(string taskId);
    Task CancelTaskAsync(string taskId);
    IReadOnlyList<BackgroundTaskInfo> GetAllTasks();
}