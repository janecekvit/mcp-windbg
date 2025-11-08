using ModelContextProtocol;
using Shared.Models;

namespace Shared.Client;

/// <summary>
/// Service for interacting with the BackgroundService HTTP API
/// </summary>
public interface IDebuggerApiService
{
    /// <summary>
    /// Checks if the background debugging service is healthy and available
    /// </summary>
    Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a memory dump file and creates a new debugging session
    /// </summary>
    /// <param name="dumpFilePath">Path to the dump file</param>
    /// <param name="progress">Progress reporter for MCP notifications</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Session ID and status message</returns>
    Task<string> LoadDumpAsync(
        string dumpFilePath,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a WinDbg/CDB command in an existing debugging session
    /// </summary>
    Task<string> ExecuteCommandAsync(
        string sessionId,
        string command,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a comprehensive basic analysis of the loaded dump
    /// </summary>
    Task<string> BasicAnalysisAsync(
        string sessionId,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a predefined analysis on the loaded dump
    /// </summary>
    Task<string> PredefinedAnalysisAsync(
        string sessionId,
        AnalysisType analysisType,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes a debugging session and frees its resources
    /// </summary>
    Task<string> CloseSessionAsync(
        string sessionId,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all jobs with their current status, optionally filtered by state
    /// </summary>
    Task<string> ListJobsAsync(
        string? state = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Detects available CDB/WinDbg installations on the system
    /// </summary>
    Task<string> DetectDebuggersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all available predefined analyses with descriptions
    /// </summary>
    Task<string> ListAnalysesAsync(CancellationToken cancellationToken = default);
}