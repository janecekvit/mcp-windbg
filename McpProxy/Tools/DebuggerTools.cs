using System.ComponentModel;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using McpProxy.Services;
using Shared.Models;

namespace McpProxy.Tools;

/// <summary>
/// MCP Tools for Windows memory dump debugging using WinDbg/CDB
/// </summary>
[McpServerToolType]
public class DebuggerTools
{
    private readonly ILogger<DebuggerTools> _logger;
    private readonly IDebuggerApiService _apiService;

    public DebuggerTools(
        ILogger<DebuggerTools> logger,
        IDebuggerApiService apiService)
    {
        _logger = logger;
        _apiService = apiService;
    }

    [McpServerTool]
    [Description("Load a memory dump file and create a new CDB debugging session")]
    public async Task<string> LoadDump(
        [Description("Path to the memory dump file (.dmp)")] string dump_file_path,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Tool load_dump called with dump_file_path: {DumpFilePath}", dump_file_path);

        try
        {
            var result = await _apiService.LoadDumpAsync(
                dump_file_path,
                progress,
                cancellationToken);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading dump: {Message}", ex.Message);
            throw new InvalidOperationException($"Failed to load dump: {ex.Message}", ex);
        }
    }

    [McpServerTool]
    [Description("Execute a WinDbg/CDB command in an existing debugging session")]
    public async Task<string> ExecuteCommand(
        [Description("ID of the debugging session")] string session_id,
        [Description("WinDbg/CDB command to execute (e.g., 'kb', '!analyze -v', 'dt')")] string command,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Tool execute_command called with session_id: {SessionId}, command: {Command}",
            session_id, command);

        try
        {
            var result = await _apiService.ExecuteCommandAsync(
                session_id,
                command,
                progress,
                cancellationToken);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing command: {Message}", ex.Message);
            throw new InvalidOperationException($"Failed to execute command: {ex.Message}", ex);
        }
    }

    [McpServerTool]
    [Description("Run a comprehensive basic analysis of the loaded dump (equivalent to the PowerShell script)")]
    public async Task<string> BasicAnalysis(
        [Description("ID of the debugging session")] string session_id,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Tool basic_analysis called with session_id: {SessionId}", session_id);

        try
        {
            var result = await _apiService.BasicAnalysisAsync(
                session_id,
                progress,
                cancellationToken);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running basic analysis: {Message}", ex.Message);
            throw new InvalidOperationException($"Failed to run basic analysis: {ex.Message}", ex);
        }
    }

    [McpServerTool]
    [Description("Run a predefined analysis on the loaded dump (basic, exception, threads, heap, modules, handles, locks, memory, drivers, processes)")]
    public async Task<string> PredefinedAnalysis(
        [Description("ID of the debugging session")] string session_id,
        [Description("Type of analysis to run")] AnalysisType analysis_type,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Tool predefined_analysis called with session_id: {SessionId}, analysis_type: {AnalysisType}",
            session_id, analysis_type);

        try
        {
            var result = await _apiService.PredefinedAnalysisAsync(
                session_id,
                analysis_type,
                progress,
                cancellationToken);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running predefined analysis: {Message}", ex.Message);
            throw new InvalidOperationException($"Failed to run predefined analysis: {ex.Message}", ex);
        }
    }

    [McpServerTool]
    [Description("Close a debugging session and free resources")]
    public async Task<string> CloseSession(
        [Description("ID of the debugging session to close")] string session_id,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Tool close_session called with session_id: {SessionId}", session_id);

        try
        {
            var result = await _apiService.CloseSessionAsync(
                session_id,
                progress,
                cancellationToken);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error closing session: {Message}", ex.Message);
            throw new InvalidOperationException($"Failed to close session: {ex.Message}", ex);
        }
    }

    [McpServerTool]
    [Description("List all jobs with their current status, optionally filtered by state (Queued, Running, Completed, Failed, Cancelled)")]
    public async Task<string> ListJobs(
        [Description("Optional filter by job state (Queued, Running, Completed, Failed, Cancelled)")] string? state = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Tool list_jobs called with state filter: {State}", state ?? "none");

        try
        {
            var result = await _apiService.ListJobsAsync(state, cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing jobs: {Message}", ex.Message);
            throw new InvalidOperationException($"Failed to list jobs: {ex.Message}", ex);
        }
    }

    [McpServerTool]
    [Description("List all available predefined analyses with descriptions")]
    public async Task<string> ListAnalyses(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Tool list_analyses called");

        try
        {
            var result = await _apiService.ListAnalysesAsync(cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing analyses: {Message}", ex.Message);
            throw new InvalidOperationException($"Failed to list analyses: {ex.Message}", ex);
        }
    }

    [McpServerTool]
    [Description("Detect available CDB/WinDbg installations on the system")]
    public async Task<string> DetectDebuggers(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Tool detect_debuggers called");

        try
        {
            var result = await _apiService.DetectDebuggersAsync(cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting debuggers: {Message}", ex.Message);
            throw new InvalidOperationException($"Failed to detect debuggers: {ex.Message}", ex);
        }
    }
}
