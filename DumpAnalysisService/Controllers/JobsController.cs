using System.Text.Json;
using DumpAnalysisService.Fingerprinting;
using DumpAnalysisService.Security;
using DumpAnalysisService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Shared.Models;

namespace DumpAnalysisService.Controllers;

[ApiController]
[Route("api/jobs")]
[Produces("application/json")]
[Authorize(Policy = DumpPolicies.Read)]
public class JobsController : ControllerBase
{
    private readonly ILogger<JobsController> _logger;
    private readonly IJobManagerService _jobManager;
    private readonly ISessionManagerService _sessionManager;
    private readonly IDumpTriageService _triage;
    private readonly ICrashFingerprintService _fingerprints;
    private readonly SecurityOptions _security;
    private readonly AuditLog _audit;

    public JobsController(
        ILogger<JobsController> logger,
        IJobManagerService jobManager,
        ISessionManagerService sessionManager,
        IDumpTriageService triage,
        ICrashFingerprintService fingerprints,
        IOptions<SecurityOptions> security,
        AuditLog audit)
    {
        _logger = logger;
        _jobManager = jobManager;
        _sessionManager = sessionManager;
        _triage = triage;
        _fingerprints = fingerprints;
        _security = security.Value;
        _audit = audit;
    }

    private Caller _Caller => Caller.From(User, _security);

    /// <summary>
    /// Gets the status of a specific job
    /// </summary>
    [HttpGet("{jobId}")]
    [ProducesResponseType<JobStatus>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public IActionResult GetJobStatus(string jobId)
    {
        try
        {
            if (!_jobManager.CanAccessJob(_Caller, jobId))
                throw new ArgumentException($"Job {jobId} not found", nameof(jobId));

            var status = _jobManager.GetJobStatus(jobId);
            return Ok(status);
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "Job not found: {JobId}", jobId);
            return Problem(
                detail: $"Job {jobId} not found",
                statusCode: StatusCodes.Status404NotFound,
                title: "Job Not Found");
        }
    }

    /// <summary>
    /// Gets all jobs, optionally filtered by state
    /// </summary>
    [HttpGet]
    [ProducesResponseType<IEnumerable<JobStatus>>(StatusCodes.Status200OK)]
    public IActionResult GetAllJobs([FromQuery] JobState? state = null)
    {
        var caller = _Caller;
        var jobs = _jobManager.GetAllJobs(state).Where(job => _jobManager.CanAccessJob(caller, job.JobId));
        return Ok(jobs);
    }

    /// <summary>
    /// Cancels a running job and terminates associated CDB process
    /// </summary>
    [HttpPost("{jobId}/cancel")]
    [Authorize(Policy = DumpPolicies.Analyze)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelJob(string jobId)
    {
        try
        {
            var caller = _Caller;
            if (!_jobManager.CanAccessJob(caller, jobId))
                throw new ArgumentException($"Job {jobId} not found", nameof(jobId));

            var status = _jobManager.GetJobStatus(jobId);
            _audit.Record(caller, "cancel-job", status.SessionId, $"job={jobId}");

            // If job has an associated session, cancel the CDB process
            if (status.SessionId != null)
            {
                try
                {
                    await _sessionManager.CancelSessionAsync(status.SessionId);
                    _logger.LogInformation("Cancelled CDB session {SessionId} for job {JobId}", status.SessionId, jobId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to cancel session {SessionId} for job {JobId}", status.SessionId, jobId);
                    // Continue with job cancellation even if session cancel fails
                }
            }

            await _jobManager.CancelJobAsync(jobId);
            return Ok(new { message = $"Job {jobId} cancelled" });
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "Job not found: {JobId}", jobId);
            return Problem(
                detail: $"Job {jobId} not found",
                statusCode: StatusCodes.Status404NotFound,
                title: "Job Not Found");
        }
    }

    /// <summary>
    /// Creates a new job to load a dump file asynchronously
    /// </summary>
    [HttpPost("load-dump")]
    [Authorize(Policy = DumpPolicies.Analyze)]
    [ProducesResponseType<JobCreatedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public IActionResult LoadDumpAsync([FromBody] LoadDumpRequest request)
    {
        var caller = _Caller;
        var jobId = _jobManager.CreateJob(JobOperationType.LoadDump, owner: caller.Id);
        _logger.LogInformation("Created job {JobId} for loading dump: {DumpFile}", jobId, request.DumpFilePath);
        _audit.Record(caller, "load-dump", detail: request.DumpFilePath);

        // Start the operation in background
        _ = Task.Run(async () =>
        {
            try
            {
                var sessionId = await _sessionManager.CreateSessionWithDumpAsync(
                    jobId,
                    request.DumpFilePath,
                    request.Symbols,
                    caller.Id);
                await _jobManager.CompleteJobAsync(jobId, sessionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {JobId} failed", jobId);
                await _jobManager.FailJobAsync(jobId, ex.Message);
            }
        });

        return _AcceptedJobResponse(jobId, "loading dump file");
    }

    /// <summary>
    /// Creates a new job to execute a command asynchronously
    /// </summary>
    [HttpPost("execute-command")]
    [Authorize(Policy = DumpPolicies.Analyze)]
    [ProducesResponseType<JobCreatedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public IActionResult ExecuteCommandAsync([FromBody] ExecuteCommandRequest request)
    {
        var caller = _Caller;
        if (!_TryEnsureSession(caller, request.SessionId, out var notFound))
            return notFound;

        var jobId = _jobManager.CreateJob(JobOperationType.ExecuteCommand, request.SessionId, caller.Id);
        _audit.Record(caller, "execute-command", request.SessionId, request.Command);
        _logger.LogInformation("Created job {JobId} for executing command in session {SessionId}", jobId, request.SessionId);

        // Start the operation in background
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _sessionManager.ExecuteCommandAsync(jobId, request.SessionId, request.Command);
                await _jobManager.CompleteJobAsync(jobId, result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {JobId} failed", jobId);
                await _jobManager.FailJobAsync(jobId, ex.Message);
            }
        });

        return _AcceptedJobResponse(jobId, "executing command");
    }

    /// <summary>
    /// Creates a new job to run basic analysis asynchronously
    /// </summary>
    [HttpPost("basic-analysis")]
    [Authorize(Policy = DumpPolicies.Analyze)]
    [ProducesResponseType<JobCreatedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public IActionResult BasicAnalysisAsync([FromBody] BasicAnalysisRequest request)
    {
        var caller = _Caller;
        if (!_TryEnsureSession(caller, request.SessionId, out var notFound))
            return notFound;

        var jobId = _jobManager.CreateJob(JobOperationType.BasicAnalysis, request.SessionId, caller.Id);
        _audit.Record(caller, "basic-analysis", request.SessionId);
        _logger.LogInformation("Created job {JobId} for basic analysis in session {SessionId}", jobId, request.SessionId);

        // Start the operation in background
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _sessionManager.ExecuteBasicAnalysisAsync(jobId, request.SessionId);
                await _jobManager.CompleteJobAsync(jobId, result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {JobId} failed", jobId);
                await _jobManager.FailJobAsync(jobId, ex.Message);
            }
        });

        return _AcceptedJobResponse(jobId, "basic analysis");
    }

    /// <summary>
    /// Creates a new job to run predefined analysis asynchronously
    /// </summary>
    [HttpPost("predefined-analysis")]
    [Authorize(Policy = DumpPolicies.Analyze)]
    [ProducesResponseType<JobCreatedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public IActionResult PredefinedAnalysisAsync([FromBody] PredefinedAnalysisRequest request)
    {
        var caller = _Caller;
        if (!_TryEnsureSession(caller, request.SessionId, out var notFound))
            return notFound;

        var jobId = _jobManager.CreateJob(JobOperationType.PredefinedAnalysis, request.SessionId, caller.Id);
        _audit.Record(caller, "predefined-analysis", request.SessionId, request.AnalysisType.ToString());
        _logger.LogInformation("Created job {JobId} for predefined analysis '{AnalysisType}' in session {SessionId}",
            jobId, request.AnalysisType, request.SessionId);

        // Start the operation in background
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _sessionManager.ExecutePredefinedAnalysisAsync(jobId, request.SessionId, request.AnalysisType);
                await _jobManager.CompleteJobAsync(jobId, result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {JobId} failed", jobId);
                await _jobManager.FailJobAsync(jobId, ex.Message);
            }
        });

        return _AcceptedJobResponse(jobId, "predefined analysis");
    }

    /// <summary>
    /// Creates a new job to close a debugging session asynchronously
    /// </summary>
    [HttpPost("close-session")]
    [Authorize(Policy = DumpPolicies.Analyze)]
    [ProducesResponseType<JobCreatedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public IActionResult CloseSessionAsync([FromBody] CloseSessionRequest request)
    {
        var caller = _Caller;
        if (!_TryEnsureSession(caller, request.SessionId, out var notFound))
            return notFound;

        var jobId = _jobManager.CreateJob(JobOperationType.CloseSession, request.SessionId, caller.Id);
        _audit.Record(caller, "close-session", request.SessionId);
        _logger.LogInformation("Created job {JobId} for closing session {SessionId}", jobId, request.SessionId);

        // Start the operation in background
        _ = Task.Run(async () =>
        {
            try
            {
                await _sessionManager.CancelSessionAsync(request.SessionId);
                await _jobManager.CompleteJobAsync(jobId, $"Session {request.SessionId} closed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {JobId} failed", jobId);
                await _jobManager.FailJobAsync(jobId, ex.Message);
            }
        });

        return _AcceptedJobResponse(jobId, "closing session");
    }

    /// <summary>
    /// Creates a job that reads a dump's minidump streams without the debugger.
    /// The job result is the triage result as JSON.
    /// </summary>
    [HttpPost("triage-dump")]
    [Authorize(Policy = DumpPolicies.Analyze)]
    [ProducesResponseType<JobCreatedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public IActionResult TriageDumpAsync([FromBody] TriageDumpRequest request)
    {
        var caller = _Caller;
        var jobId = _jobManager.CreateJob(JobOperationType.TriageDump, owner: caller.Id);
        _logger.LogInformation("Created job {JobId} for triaging dump: {DumpFile}", jobId, request.DumpFilePath);
        _audit.Record(caller, "triage-dump", detail: request.DumpFilePath);

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _triage.TriageAsync(request.DumpFilePath, request.SymbolCache);
                await _jobManager.CompleteJobAsync(jobId, JsonSerializer.Serialize(result, JsonSerializerOptions.Web));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {JobId} failed", jobId);
                await _jobManager.FailJobAsync(jobId, ex.Message);
            }
        });

        return _AcceptedJobResponse(jobId, "dump triage");
    }

    /// <summary>
    /// Creates a job that fingerprints the crash in a session and records it in the crash group registry.
    /// The job result is the fingerprint result as JSON.
    /// </summary>
    [HttpPost("fingerprint-crash")]
    [Authorize(Policy = DumpPolicies.Analyze)]
    [ProducesResponseType<JobCreatedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public IActionResult FingerprintCrashAsync([FromBody] FingerprintCrashRequest request)
    {
        var caller = _Caller;
        if (!_TryEnsureSession(caller, request.SessionId, out var notFound))
            return notFound;

        var jobId = _jobManager.CreateJob(JobOperationType.FingerprintCrash, request.SessionId, caller.Id);
        _logger.LogInformation("Created job {JobId} for fingerprinting the crash in session {SessionId}", jobId, request.SessionId);
        _audit.Record(caller, "fingerprint-crash", request.SessionId);

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _fingerprints.FingerprintAsync(jobId, request.SessionId);
                await _jobManager.CompleteJobAsync(jobId, JsonSerializer.Serialize(result, JsonSerializerOptions.Web));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {JobId} failed", jobId);
                await _jobManager.FailJobAsync(jobId, ex.Message);
            }
        });

        return _AcceptedJobResponse(jobId, "crash fingerprint");
    }

    /// <summary>A restricted caller gets 404 for another caller's session, as for one that does not exist.</summary>
    private bool _TryEnsureSession(Caller caller, string sessionId, out IActionResult notFound)
    {
        try
        {
            _sessionManager.EnsureSessionAccess(caller, sessionId);
            notFound = null!;
            return true;
        }
        catch (ArgumentException)
        {
            _logger.LogWarning("Denied {Caller} access to session {SessionId}", caller.Name, sessionId);
            notFound = Problem(
                detail: $"Session {sessionId} not found",
                statusCode: StatusCodes.Status404NotFound,
                title: "Session Not Found");
            return false;
        }
    }

    /// <summary>
    /// Creates a standardized 202 Accepted response for job creation
    /// </summary>
    private AcceptedResult _AcceptedJobResponse(string jobId, string operationDescription)
    {
        var response = new JobCreatedResponse(
            jobId,
            jobId.ToJobEndpoint(),
            $"Job {jobId} created for {operationDescription}");

        return Accepted(jobId.ToJobEndpoint(), response);
    }
}
