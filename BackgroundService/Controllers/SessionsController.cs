using BackgroundService.Services;
using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Models;

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

