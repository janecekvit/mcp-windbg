using BackgroundService.Models;

namespace BackgroundService.Services;

public interface ISessionManagerService : IDisposable
{
    /// <summary>
    /// Creates a new debugging session and loads the specified dump file with job-based progress reporting
    /// </summary>
    /// <param name="jobId">Job ID for progress tracking</param>
    /// <param name="dumpFilePath">Path to the memory dump file to load</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Unique session ID for the created session</returns>
    Task<string> CreateSessionWithDumpAsync(string jobId, string dumpFilePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a WinDbg/CDB command in the specified session with job-based progress reporting
    /// </summary>
    /// <param name="jobId">Job ID for progress tracking</param>
    /// <param name="sessionId">ID of the session to execute the command in</param>
    /// <param name="command">The debugger command to execute</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Output from the debugger command</returns>
    Task<string> ExecuteCommandAsync(string jobId, string sessionId, string command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a comprehensive basic analysis in the specified session with job-based progress reporting
    /// </summary>
    /// <param name="jobId">Job ID for progress tracking</param>
    /// <param name="sessionId">ID of the session to analyze</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Analysis output from the debugger</returns>
    Task<string> ExecuteBasicAnalysisAsync(string jobId, string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a predefined analysis in the specified session with job-based progress reporting
    /// </summary>
    /// <param name="jobId">Job ID for progress tracking</param>
    /// <param name="sessionId">ID of the session to analyze</param>
    /// <param name="analysisName">Name of the predefined analysis to run</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Analysis output from the debugger</returns>
    Task<string> ExecutePredefinedAnalysisAsync(string jobId, string sessionId, string analysisName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes a debugging session and frees its resources
    /// </summary>
    /// <param name="sessionId">ID of the session to close</param>
    void CloseSession(string sessionId);

    /// <summary>
    /// Cancels a running session and terminates its CDB process
    /// </summary>
    /// <param name="sessionId">ID of the session to cancel</param>
    Task CancelSessionAsync(string sessionId);

    /// <summary>
    /// Gets information about all currently active debugging sessions
    /// </summary>
    /// <returns>Enumerable of session information objects</returns>
    IEnumerable<BackgroundService.Models.SessionInfo> GetActiveSessions();
}