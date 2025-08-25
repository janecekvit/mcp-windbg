using BackgroundService.Models;

namespace BackgroundService.Services;

public interface ISessionManagerService : IDisposable
{
    Task<string> CreateSessionWithDumpAsync(string dumpFilePath);
    Task<string> ExecuteCommandAsync(string sessionId, string command);
    Task<string> ExecuteBasicAnalysisAsync(string sessionId);
    Task<string> ExecutePredefinedAnalysisAsync(string sessionId, string analysisName);
    void CloseSession(string sessionId);
    IEnumerable<SessionInfo> GetActiveSessions();
}