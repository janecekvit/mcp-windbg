using BackgroundService.Services;
using Common;
using Microsoft.AspNetCore.Mvc;

namespace BackgroundService.Controllers;

[ApiController]
[Route("api/sessions")]
[Produces("application/json")]
public class SessionsController : ControllerBase
{
    private readonly ILogger<SessionsController> _logger;
    private readonly ISessionManagerService _sessionManager;

    public SessionsController(ILogger<SessionsController> logger, ISessionManagerService sessionManager)
    {
        _logger = logger;
        _sessionManager = sessionManager;
    }

    /// <summary>
    /// Creates a new debugging session and loads a memory dump file
    /// </summary>
    /// <param name="request">The dump loading request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Session creation response with metadata</returns>
    [HttpPost("load-dump")]
    [ProducesResponseType<LoadDumpResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> LoadDump([FromBody] LoadDumpRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var sessionId = await _sessionManager.CreateSessionWithDumpAsync(request.DumpFilePath, cancellationToken);
            _logger.LogInformation("Created session {SessionId} for dump: {DumpFile}", sessionId, request.DumpFilePath);

            return Ok(new LoadDumpResponse(sessionId, $"Session {sessionId} created successfully", request.DumpFilePath));
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogError(ex, "Dump file not found: {DumpFile}", request.DumpFilePath);
            return Problem(
                detail: $"Dump file not found: {request.DumpFilePath}",
                statusCode: Constants.Http.BadRequest,
                title: "Dump File Not Found");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading dump file: {DumpFile}", request.DumpFilePath);
            return Problem(
                detail: $"Error loading dump: {ex.Message}",
                statusCode: Constants.Http.InternalServerError,
                title: "Dump Loading Failed");
        }
    }

    /// <summary>
    /// Executes a WinDbg/CDB command in an existing debugging session
    /// </summary>
    /// <param name="request">The command execution request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Command execution response with output</returns>
    [HttpPost("execute-command")]
    [ProducesResponseType<CommandExecutionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ExecuteCommand([FromBody] ExecuteCommandRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _sessionManager.ExecuteCommandAsync(request.SessionId, request.Command, cancellationToken);
            return Ok(new CommandExecutionResponse(result));
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "Session not found: {SessionId}", request.SessionId);
            return Problem(
                detail: $"Session {request.SessionId} not found",
                statusCode: Constants.Http.NotFound,
                title: "Session Not Found");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Session not active: {SessionId}", request.SessionId);
            return Problem(
                detail: $"Session {request.SessionId} is not active",
                statusCode: Constants.Http.BadRequest,
                title: "Session Not Active");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing command in session {SessionId}: {Command}", request.SessionId, request.Command);
            return Problem(
                detail: $"Error executing command: {ex.Message}",
                statusCode: Constants.Http.InternalServerError,
                title: "Command Execution Failed");
        }
    }

    /// <summary>
    /// Runs a comprehensive basic analysis on the loaded dump
    /// </summary>
    /// <param name="request">The basic analysis request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Analysis results</returns>
    [HttpPost("basic-analysis")]
    [ProducesResponseType<CommandExecutionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> BasicAnalysis([FromBody] BasicAnalysisRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _sessionManager.ExecuteBasicAnalysisAsync(request.SessionId, cancellationToken);
            return Ok(new CommandExecutionResponse(result));
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "Session not found: {SessionId}", request.SessionId);
            return Problem(
                detail: $"Session {request.SessionId} not found",
                statusCode: Constants.Http.NotFound,
                title: "Session Not Found");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing basic analysis in session {SessionId}", request.SessionId);
            return Problem(
                detail: $"Error executing analysis: {ex.Message}",
                statusCode: Constants.Http.InternalServerError,
                title: "Analysis Execution Failed");
        }
    }

    /// <summary>
    /// Executes a predefined analysis on the loaded dump
    /// </summary>
    /// <param name="request">The predefined analysis request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Analysis results</returns>
    [HttpPost("predefined-analysis")]
    [ProducesResponseType<CommandExecutionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> PredefinedAnalysis([FromBody] PredefinedAnalysisRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _sessionManager.ExecutePredefinedAnalysisAsync(request.SessionId, request.AnalysisType, cancellationToken);
            return Ok(new CommandExecutionResponse(result));
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "Session not found or invalid analysis: {SessionId}", request.SessionId);
            return Problem(
                detail: $"Session {request.SessionId} not found or invalid analysis type",
                statusCode: Constants.Http.NotFound,
                title: "Session Not Found or Invalid Analysis");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing predefined analysis in session {SessionId}: {AnalysisType}", request.SessionId, request.AnalysisType);
            return Problem(
                detail: $"Error executing analysis: {ex.Message}",
                statusCode: Constants.Http.InternalServerError,
                title: "Analysis Execution Failed");
        }
    }

    /// <summary>
    /// Lists all active debugging sessions
    /// </summary>
    /// <returns>List of active sessions</returns>
    [HttpGet]
    [ProducesResponseType<SessionsResponse>(StatusCodes.Status200OK)]
    public IActionResult GetSessions()
    {
        var sessions = _sessionManager.GetActiveSessions()
            .Select(s => new SessionInfo(s.SessionId, s.DumpFile, s.IsActive))
            .ToList();

        return Ok(new SessionsResponse(sessions));
    }

    /// <summary>
    /// Closes a debugging session and frees its resources
    /// </summary>
    /// <param name="sessionId">The session ID to close</param>
    /// <returns>Session closure confirmation</returns>
    [HttpDelete("{sessionId}")]
    [ProducesResponseType<CloseSessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public IActionResult CloseSession(string sessionId)
    {
        try
        {
            _sessionManager.CloseSession(sessionId);
            _logger.LogInformation("Closed session: {SessionId}", sessionId);
            return Ok(new CloseSessionResponse($"Session {sessionId} closed successfully"));
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "Session not found: {SessionId}", sessionId);
            return Problem(
                detail: $"Session {sessionId} not found",
                statusCode: Constants.Http.NotFound,
                title: "Session Not Found");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error closing session: {SessionId}", sessionId);
            return Problem(
                detail: $"Error closing session: {ex.Message}",
                statusCode: Constants.Http.InternalServerError,
                title: "Session Closure Failed");
        }
    }
}

