using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using DumpAnalysisService.Providers;
using DumpAnalysisService.Services;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Shared;
using Shared.Models;

namespace DumpAnalysisService.Tools;

/// <summary>
/// MCP Tools for Windows memory dump debugging using WinDbg/CDB
/// These tools create jobs and wait for completion, forwarding progress to MCP client
/// </summary>
[McpServerToolType]
public class DebuggerTools
{
    private readonly ILogger<DebuggerTools> _logger;
    private readonly IJobManagerService _jobManager;
    private readonly ISessionManagerService _sessionManager;
    private readonly IDiagnosticsService _diagnosticsService;
    private readonly ISymbolConfigurationProvider _symbolConfigProvider;

    public DebuggerTools(
        ILogger<DebuggerTools> logger,
        IJobManagerService jobManager,
        ISessionManagerService sessionManager,
        IDiagnosticsService diagnosticsService,
        ISymbolConfigurationProvider symbolConfigProvider)
    {
        _logger = logger;
        _jobManager = jobManager;
        _sessionManager = sessionManager;
        _diagnosticsService = diagnosticsService;
        _symbolConfigProvider = symbolConfigProvider;
    }

    [McpServerTool]
    [Description("Load a memory dump file and create a new CDB debugging session")]
    public async Task<string> LoadDump(
        [Description("Path to the memory dump file (.dmp)")] string dump_file_path,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Tool load_dump called with dump_file_path: {DumpFilePath}", dump_file_path);

        // Validate dump file
        if (string.IsNullOrWhiteSpace(dump_file_path))
            throw new ArgumentException("Dump file path is required", nameof(dump_file_path));

        try
        {
            if (!File.Exists(dump_file_path))
                throw new FileNotFoundException($"Dump file not found: {dump_file_path}", dump_file_path);

            var symbols = _symbolConfigProvider.GetConfiguration();

            // Create job
            var jobId = _jobManager.CreateJob(JobOperationType.LoadDump);
            _logger.LogInformation("Created job {JobId} for loading dump", jobId);

            // Start background operation
            _ = Task.Run(async () =>
            {
                try
                {
                    var sessionId = await _sessionManager.CreateSessionWithDumpAsync(
                        jobId,
                        dump_file_path,
                        symbols,
                        cancellationToken);
                    await _jobManager.CompleteJobAsync(jobId, sessionId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Job {JobId} failed", jobId);
                    await _jobManager.FailJobAsync(jobId, ex.Message);
                }
            }, cancellationToken);

            // Wait for job completion with progress reporting
            return await _WaitForJobCompletionAsync(jobId, progress, cancellationToken);
        }
        catch (FileNotFoundException ex)
        {
            // Domain-meaningful translation: surface a friendlier error message to MCP clients
            _logger.LogError(ex, "Dump file not found: {Message}", ex.Message);
            throw new InvalidOperationException(
                $"Dump file not found: {ex.FileName ?? dump_file_path}", ex);
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

        if (string.IsNullOrWhiteSpace(session_id))
            throw new ArgumentException("Session ID is required", nameof(session_id));

        if (string.IsNullOrWhiteSpace(command))
            throw new ArgumentException("Command is required", nameof(command));

        var jobId = _jobManager.CreateJob(JobOperationType.ExecuteCommand, session_id);
        _logger.LogInformation("Created job {JobId} for executing command", jobId);

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _sessionManager.ExecuteCommandAsync(jobId, session_id, command, cancellationToken);
                await _jobManager.CompleteJobAsync(jobId, result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {JobId} failed", jobId);
                await _jobManager.FailJobAsync(jobId, ex.Message);
            }
        }, cancellationToken);

        return await _WaitForJobCompletionAsync(jobId, progress, cancellationToken);
    }

    [McpServerTool]
    [Description("Run a comprehensive basic analysis of the loaded dump (equivalent to the PowerShell script)")]
    public async Task<string> BasicAnalysis(
        [Description("ID of the debugging session")] string session_id,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Tool basic_analysis called with session_id: {SessionId}", session_id);

        if (string.IsNullOrWhiteSpace(session_id))
            throw new ArgumentException("Session ID is required", nameof(session_id));

        var jobId = _jobManager.CreateJob(JobOperationType.BasicAnalysis, session_id);
        _logger.LogInformation("Created job {JobId} for basic analysis", jobId);

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _sessionManager.ExecuteBasicAnalysisAsync(jobId, session_id, cancellationToken);
                await _jobManager.CompleteJobAsync(jobId, result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {JobId} failed", jobId);
                await _jobManager.FailJobAsync(jobId, ex.Message);
            }
        }, cancellationToken);

        return await _WaitForJobCompletionAsync(jobId, progress, cancellationToken);
    }

    [McpServerTool]
    [Description("Run a predefined analysis on the loaded dump (basic, exception, threads, heap, modules, handles, locks, memory, drivers, processes)")]
    public async Task<string> PredefinedAnalysis(
        [Description("ID of the debugging session")] string session_id,
        [Description("Type of analysis to run")]
        [AllowedValues("basic", "exception", "threads", "heap", "modules", "handles", "locks", "memory", "drivers", "processes")]
        string analysis_type,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Tool predefined_analysis called with session_id: {SessionId}, analysis_type: {AnalysisType}",
            session_id, analysis_type);

        if (string.IsNullOrWhiteSpace(session_id))
            throw new ArgumentException("Session ID is required", nameof(session_id));

        if (string.IsNullOrWhiteSpace(analysis_type))
            throw new ArgumentException("Analysis type is required", nameof(analysis_type));

        var parsedAnalysisType = analysis_type.ToAnalysisType();

        var jobId = _jobManager.CreateJob(JobOperationType.PredefinedAnalysis, session_id);
        _logger.LogInformation("Created job {JobId} for predefined analysis", jobId);

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _sessionManager.ExecutePredefinedAnalysisAsync(jobId, session_id, parsedAnalysisType, cancellationToken);
                await _jobManager.CompleteJobAsync(jobId, result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {JobId} failed", jobId);
                await _jobManager.FailJobAsync(jobId, ex.Message);
            }
        }, cancellationToken);

        return await _WaitForJobCompletionAsync(jobId, progress, cancellationToken);
    }

    [McpServerTool]
    [Description("Close a debugging session and free resources")]
    public async Task<string> CloseSession(
        [Description("ID of the debugging session to close")] string session_id,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Tool close_session called with session_id: {SessionId}", session_id);

        if (string.IsNullOrWhiteSpace(session_id))
            throw new ArgumentException("Session ID is required", nameof(session_id));

        var jobId = _jobManager.CreateJob(JobOperationType.CloseSession, session_id);
        _logger.LogInformation("Created job {JobId} for closing session", jobId);

        _ = Task.Run(async () =>
        {
            try
            {
                await _sessionManager.CancelSessionAsync(session_id);
                await _jobManager.CompleteJobAsync(jobId, $"Session {session_id} closed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {JobId} failed", jobId);
                await _jobManager.FailJobAsync(jobId, ex.Message);
            }
        }, cancellationToken);

        return await _WaitForJobCompletionAsync(jobId, progress, cancellationToken);
    }

    [McpServerTool]
    [Description("List all jobs with their current status, optionally filtered by state (Queued, Running, Completed, Failed, Cancelled)")]
    public Task<string> ListJobs(
        [Description("Optional filter by job state (Queued, Running, Completed, Failed, Cancelled)")] string? state = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Tool list_jobs called with state filter: {State}", state ?? "none");

        JobState? jobState = null;
        if (!string.IsNullOrWhiteSpace(state))
        {
            if (!Enum.TryParse<JobState>(state, ignoreCase: true, out var parsedState))
                throw new ArgumentException($"Invalid job state: {state}. Valid values: Queued, Running, Completed, Failed, Cancelled", nameof(state));
            jobState = parsedState;
        }

        var jobs = _jobManager.GetAllJobs(jobState);

        // Format as readable text
        var sb = new StringBuilder();
        sb.AppendLine($"Jobs ({jobs.Count()}):");
        sb.AppendLine();

        foreach (var job in jobs)
        {
            var statusEmoji = job.State switch
            {
                JobState.Queued => "Queued",
                JobState.Running => "Running",
                JobState.Completed => "Completed",
                JobState.Failed => "Failed",
                JobState.Cancelled => "Cancelled",
                _ => "Unknown"
            };

            sb.AppendLine($"Job {job.JobId}:");
            sb.AppendLine($"  State: {statusEmoji}");
            sb.AppendLine($"  Progress: {job.Progress:F1}%");
            if (!string.IsNullOrWhiteSpace(job.SessionId))
                sb.AppendLine($"  Session: {job.SessionId}");
            if (!string.IsNullOrWhiteSpace(job.Message))
                sb.AppendLine($"  Message: {job.Message}");
            sb.AppendLine();
        }

        return Task.FromResult(sb.ToString());
    }

    [McpServerTool]
    [Description("List all available predefined analyses with descriptions")]
    public Task<string> ListAnalyses(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Tool list_analyses called");

        var analyses = _diagnosticsService.GetAvailableAnalyses();

        // Format as readable text
        var sb = new StringBuilder();
        sb.AppendLine($"Available Analyses ({analyses.Count}):");
        sb.AppendLine();

        foreach (var analysis in analyses)
        {
            sb.AppendLine($"• {analysis.Name}");
            sb.AppendLine($"  {analysis.Description}");
            sb.AppendLine();
        }

        return Task.FromResult(sb.ToString());
    }

    [McpServerTool]
    [Description("Detect available CDB/WinDbg installations on the system")]
    public Task<string> DetectDebuggers(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Tool detect_debuggers called");

        var result = _diagnosticsService.DetectDebuggers();

        // Format as readable text
        var sb = new StringBuilder();
        sb.AppendLine("Debugger Detection:");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(result.CdbPath))
            sb.AppendLine($"CDB: {result.CdbPath}");
        else
            sb.AppendLine("CDB: Not found");

        if (result.FoundPaths.Any())
        {
            sb.AppendLine();
            sb.AppendLine("Other detected paths:");
            foreach (var path in result.FoundPaths)
                sb.AppendLine($"  • {path}");
        }

        return Task.FromResult(sb.ToString());
    }

    /// <summary>
    /// Waits for a job to complete, polling job status and forwarding progress to MCP client
    /// </summary>
    private async Task<string> _WaitForJobCompletionAsync(
        string jobId,
        IProgress<ProgressNotificationValue>? progress,
        CancellationToken cancellationToken)
    {
        var pollInterval = TimeSpan.FromMilliseconds(Constants.Jobs.DefaultPollIntervalMs);
        var maxWaitTime = TimeSpan.FromMilliseconds(Constants.Jobs.DefaultMaxWaitTimeMs);
        var startTime = DateTime.UtcNow;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Check timeout
            if (DateTime.UtcNow - startTime > maxWaitTime)
                throw new TimeoutException($"Job {jobId} timed out after {maxWaitTime.TotalMinutes} minutes");

            // Get job status
            var jobStatus = _jobManager.GetJobStatus(jobId);

            // Report progress to MCP client
            progress?.Report(new ProgressNotificationValue
            {
                Progress = (float)jobStatus.Progress,
                Total = 100.0f,
                Message = jobStatus.Message
            });

            // Check if job is complete
            if (jobStatus.State == JobState.Completed)
            {
                _logger.LogInformation("Job {JobId} completed successfully", jobId);
                return jobStatus.Result ?? "Operation completed successfully";
            }

            if (jobStatus.State == JobState.Failed)
            {
                var errorMessage = jobStatus.Error ?? "Unknown error";
                _logger.LogError("Job {JobId} failed: {Error}", jobId, errorMessage);
                throw new InvalidOperationException($"Job failed: {errorMessage}");
            }

            if (jobStatus.State == JobState.Cancelled)
            {
                _logger.LogWarning("Job {JobId} was cancelled", jobId);
                throw new OperationCanceledException($"Job {jobId} was cancelled");
            }

            // Wait before next poll
            await Task.Delay(pollInterval, cancellationToken);
        }
    }
}
