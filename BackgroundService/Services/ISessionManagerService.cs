using CdbBackgroundService.Models;

namespace CdbBackgroundService.Services;

public interface ISessionManagerService : IDisposable
{
    Task<(bool Success, string SessionId, string Message)> CreateSessionWithDumpAsync(string dumpFilePath);
    Task<(bool Success, string Message)> ExecuteCommandAsync(string sessionId, string command);
    Task<(bool Success, string Message)> ExecuteBasicAnalysisAsync(string sessionId);
    Task<(bool Success, string Message)> ExecutePredefinedAnalysisAsync(string sessionId, string analysisName);
    (bool Success, string Message) CloseSession(string sessionId);
    IEnumerable<SessionInfo> GetActiveSessions();
}