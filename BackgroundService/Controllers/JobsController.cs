using BackgroundService.Services;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace BackgroundService.Controllers;

[ApiController]
[Route("api/jobs")]
[Produces("application/json")]
public class JobsController : ControllerBase
{
    private readonly ILogger<JobsController> _logger;
    private readonly IJobManagerService _jobManager;
    private readonly ISessionManagerService _sessionManager;

    public JobsController(
        ILogger<JobsController> logger,
        IJobManagerService jobManager,
        ISessionManagerService sessionManager)
    {
        _logger = logger;
        _jobManager = jobManager;
        _sessionManager = sessionManager;
    }

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
        var jobs = _jobManager.GetAllJobs(state);
        return Ok(jobs);
    }

    /// <summary>
    /// Cancels a running job and terminates associated CDB process
    /// </summary>
    [HttpPost("{jobId}/cancel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelJob(string jobId)
    {
        try
        {
            var status = _jobManager.GetJobStatus(jobId);

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
    [ProducesResponseType<JobCreatedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public IActionResult LoadDumpAsync([FromBody] LoadDumpRequest request)
    {
        var jobId = _jobManager.CreateJob(JobOperationType.LoadDump);
        _logger.LogInformation("Created job {JobId} for loading dump: {DumpFile}", jobId, request.DumpFilePath);

        // Start the operation in background
        _ = Task.Run(async () =>
        {
            try
            {
                var sessionId = await _sessionManager.CreateSessionWithDumpAsync(
                    jobId,
                    request.DumpFilePath,
                    request.Symbols);
                await _jobManager.CompleteJobAsync(jobId, sessionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {JobId} failed", jobId);
                await _jobManager.FailJobAsync(jobId, ex.Message);
            }
        });

        var response = new JobCreatedResponse(
            jobId,
            $"/api/jobs/{jobId}",
            $"Job {jobId} created for loading dump file");

        return Accepted($"/api/jobs/{jobId}", response);
    }

    /// <summary>
    /// Creates a new job to execute a command asynchronously
    /// </summary>
    [HttpPost("execute-command")]
    [ProducesResponseType<JobCreatedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public IActionResult ExecuteCommandAsync([FromBody] ExecuteCommandRequest request)
    {
        var jobId = _jobManager.CreateJob(JobOperationType.ExecuteCommand, request.SessionId);
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

        var response = new JobCreatedResponse(
            jobId,
            $"/api/jobs/{jobId}",
            $"Job {jobId} created for executing command");

        return Accepted($"/api/jobs/{jobId}", response);
    }

    /// <summary>
    /// Creates a new job to run basic analysis asynchronously
    /// </summary>
    [HttpPost("basic-analysis")]
    [ProducesResponseType<JobCreatedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public IActionResult BasicAnalysisAsync([FromBody] BasicAnalysisRequest request)
    {
        var jobId = _jobManager.CreateJob(JobOperationType.BasicAnalysis, request.SessionId);
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

        var response = new JobCreatedResponse(
            jobId,
            $"/api/jobs/{jobId}",
            $"Job {jobId} created for basic analysis");

        return Accepted($"/api/jobs/{jobId}", response);
    }

    /// <summary>
    /// Creates a new job to run predefined analysis asynchronously
    /// </summary>
    [HttpPost("predefined-analysis")]
    [ProducesResponseType<JobCreatedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public IActionResult PredefinedAnalysisAsync([FromBody] PredefinedAnalysisRequest request)
    {
        var jobId = _jobManager.CreateJob(JobOperationType.PredefinedAnalysis, request.SessionId);
        _logger.LogInformation("Created job {JobId} for predefined analysis in session {SessionId}", jobId, request.SessionId);

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

        var response = new JobCreatedResponse(
            jobId,
            $"/api/jobs/{jobId}",
            $"Job {jobId} created for predefined analysis");

        return Accepted($"/api/jobs/{jobId}", response);
    }

    /// <summary>
    /// Creates a new job to close a debugging session asynchronously
    /// </summary>
    [HttpPost("close-session")]
    [ProducesResponseType<JobCreatedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public IActionResult CloseSessionAsync([FromBody] CloseSessionRequest request)
    {
        var jobId = _jobManager.CreateJob(JobOperationType.CloseSession, request.SessionId);
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

        var response = new JobCreatedResponse(
            jobId,
            $"/api/jobs/{jobId}",
            $"Job {jobId} created for closing session");

        return Accepted($"/api/jobs/{jobId}", response);
    }
}
