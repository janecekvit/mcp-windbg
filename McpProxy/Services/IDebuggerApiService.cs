using System.Text.Json;
using McpProxy.Models;

namespace McpProxy.Services;

public interface IDebuggerApiService
{
    /// <summary>
    /// Checks if the background debugging service is healthy and available
    /// </summary>
    /// <returns>True if the service is available, false otherwise</returns>
    Task<bool> CheckHealthAsync();
    
    /// <summary>
    /// Loads a memory dump file and creates a new debugging session
    /// </summary>
    /// <param name="args">JSON arguments containing dump_file_path</param>
    /// <param name="progressToken">Optional progress token for notifications</param>
    /// <returns>Result containing session ID and status information</returns>
    Task<McpToolResult> LoadDumpAsync(JsonElement args, string? progressToken = null);
    
    /// <summary>
    /// Executes a WinDbg/CDB command in an existing debugging session
    /// </summary>
    /// <param name="args">JSON arguments containing session_id and command</param>
    /// <param name="progressToken">Optional progress token for notifications</param>
    /// <returns>Result containing the command output</returns>
    Task<McpToolResult> ExecuteCommandAsync(JsonElement args, string? progressToken = null);
    
    /// <summary>
    /// Runs a comprehensive basic analysis of the loaded dump
    /// </summary>
    /// <param name="args">JSON arguments containing session_id</param>
    /// <param name="progressToken">Optional progress token for notifications</param>
    /// <returns>Result containing the analysis output</returns>
    Task<McpToolResult> BasicAnalysisAsync(JsonElement args, string? progressToken = null);
    
    /// <summary>
    /// Runs a predefined analysis on the loaded dump
    /// </summary>
    /// <param name="args">JSON arguments containing session_id and analysis_type</param>
    /// <param name="progressToken">Optional progress token for notifications</param>
    /// <returns>Result containing the analysis output</returns>
    Task<McpToolResult> PredefinedAnalysisAsync(JsonElement args, string? progressToken = null);
    
    /// <summary>
    /// Lists all active debugging sessions
    /// </summary>
    /// <returns>Result containing session information</returns>
    Task<McpToolResult> ListSessionsAsync();
    
    /// <summary>
    /// Closes a debugging session and frees its resources
    /// </summary>
    /// <param name="args">JSON arguments containing session_id</param>
    /// <returns>Result containing the closure status</returns>
    Task<McpToolResult> CloseSessionAsync(JsonElement args);
    
    /// <summary>
    /// Detects available CDB/WinDbg installations on the system
    /// </summary>
    /// <returns>Result containing detected debugger paths and environment variables</returns>
    Task<McpToolResult> DetectDebuggersAsync();
    
    /// <summary>
    /// Lists all available predefined analyses with descriptions
    /// </summary>
    /// <returns>Result containing analysis types and their descriptions</returns>
    Task<McpToolResult> ListAnalysesAsync();
}