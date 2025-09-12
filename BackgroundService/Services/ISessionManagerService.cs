using BackgroundService.Models;

namespace BackgroundService.Services;

public interface ISessionManagerService : IDisposable
{
    /// <summary>
    /// Creates a new debugging session and loads the specified dump file
    /// </summary>
    /// <param name="dumpFilePath">Path to the memory dump file to load</param>
    /// <returns>Unique session ID for the created session</returns>
    Task<string> CreateSessionWithDumpAsync(string dumpFilePath);
    
    /// <summary>
    /// Executes a WinDbg/CDB command in the specified session
    /// </summary>
    /// <param name="sessionId">ID of the session to execute the command in</param>
    /// <param name="command">The debugger command to execute</param>
    /// <returns>Output from the debugger command</returns>
    Task<string> ExecuteCommandAsync(string sessionId, string command);
    
    /// <summary>
    /// Runs a comprehensive basic analysis in the specified session
    /// </summary>
    /// <param name="sessionId">ID of the session to analyze</param>
    /// <returns>Analysis output from the debugger</returns>
    Task<string> ExecuteBasicAnalysisAsync(string sessionId);
    
    /// <summary>
    /// Executes a predefined analysis in the specified session
    /// </summary>
    /// <param name="sessionId">ID of the session to analyze</param>
    /// <param name="analysisName">Name of the predefined analysis to run</param>
    /// <returns>Analysis output from the debugger</returns>
    Task<string> ExecutePredefinedAnalysisAsync(string sessionId, string analysisName);
    
    /// <summary>
    /// Closes a debugging session and frees its resources
    /// </summary>
    /// <param name="sessionId">ID of the session to close</param>
    void CloseSession(string sessionId);
    
    /// <summary>
    /// Gets information about all currently active debugging sessions
    /// </summary>
    /// <returns>Enumerable of session information objects</returns>
    IEnumerable<SessionInfo> GetActiveSessions();
}