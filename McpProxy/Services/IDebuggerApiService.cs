using System.Text.Json;
using McpProxy.Models;

namespace McpProxy.Services;

public interface IDebuggerApiService
{
    /// <summary>
    /// Checks if the background debugging service is healthy and available
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>True if the service is available, false otherwise</returns>
    Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Loads a memory dump file and creates a new debugging session
    /// </summary>
    /// <param name="args">JSON arguments containing dump_file_path</param>
    /// <param name="progressToken">Optional progress token for notifications</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Result containing session ID and status information</returns>
    Task<McpToolResult> LoadDumpAsync(JsonElement args, string? progressToken = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Executes a WinDbg/CDB command in an existing debugging session
    /// </summary>
    /// <param name="args">JSON arguments containing session_id and command</param>
    /// <param name="progressToken">Optional progress token for notifications</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Result containing the command output</returns>
    Task<McpToolResult> ExecuteCommandAsync(JsonElement args, string? progressToken = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Runs a comprehensive basic analysis of the loaded dump
    /// </summary>
    /// <param name="args">JSON arguments containing session_id</param>
    /// <param name="progressToken">Optional progress token for notifications</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Result containing the analysis output</returns>
    Task<McpToolResult> BasicAnalysisAsync(JsonElement args, string? progressToken = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Runs a predefined analysis on the loaded dump
    /// </summary>
    /// <param name="args">JSON arguments containing session_id and analysis_type</param>
    /// <param name="progressToken">Optional progress token for notifications</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Result containing the analysis output</returns>
    Task<McpToolResult> PredefinedAnalysisAsync(JsonElement args, string? progressToken = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Lists all active debugging sessions
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Result containing session information</returns>
    Task<McpToolResult> ListSessionsAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Closes a debugging session and frees its resources
    /// </summary>
    /// <param name="args">JSON arguments containing session_id</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Result containing the closure status</returns>
    Task<McpToolResult> CloseSessionAsync(JsonElement args, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Detects available CDB/WinDbg installations on the system
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Result containing detected debugger paths and environment variables</returns>
    Task<McpToolResult> DetectDebuggersAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Lists all available predefined analyses with descriptions
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Result containing analysis types and their descriptions</returns>
    Task<McpToolResult> ListAnalysesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the status of a background task
    /// </summary>
    /// <param name="args">JSON arguments containing task_id</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Result containing task status information</returns>
    Task<McpToolResult> GetTaskStatusAsync(JsonElement args, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all background tasks
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Result containing all background tasks information</returns>
    Task<McpToolResult> ListBackgroundTasksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a background task
    /// </summary>
    /// <param name="args">JSON arguments containing task_id</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Result containing cancellation confirmation</returns>
    Task<McpToolResult> CancelTaskAsync(JsonElement args, CancellationToken cancellationToken = default);
}