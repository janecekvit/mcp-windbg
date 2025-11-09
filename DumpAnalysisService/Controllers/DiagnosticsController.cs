using System.Reflection;
using DumpAnalysisService.Services;
using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Models;

namespace DumpAnalysisService.Controllers;

[ApiController]
[Route("api/diagnostics")]
[Produces("application/json")]
public class DiagnosticsController : ControllerBase
{
    private readonly ILogger<DiagnosticsController> _logger;
    private readonly IDiagnosticsService _diagnosticsService;
    private readonly IConfiguration _configuration;

    public DiagnosticsController(
        ILogger<DiagnosticsController> logger,
        IDiagnosticsService diagnosticsService,
        IConfiguration configuration)
    {
        _logger = logger;
        _diagnosticsService = diagnosticsService;
        _configuration = configuration;
    }

    /// <summary>
    /// Health check endpoint to verify the service is running
    /// </summary>
    /// <returns>Health status information</returns>
    [HttpGet("health")]
    [ProducesResponseType<object>(StatusCodes.Status200OK)]
    public IActionResult Health()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version?.ToString() ?? "Unknown";
        var buildDate = System.IO.File.GetLastWriteTime(AppContext.BaseDirectory);

        return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
    }

    /// <summary>
    /// Detects available CDB/WinDbg installations on the system
    /// </summary>
    /// <returns>Detected debugger paths and environment information</returns>
    [HttpGet("detect-debuggers")]
    [ProducesResponseType<DebuggerDetectionResponse>(StatusCodes.Status200OK)]
    public IActionResult DetectDebuggers()
    {
        try
        {
            var result = _diagnosticsService.DetectDebuggers();
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in debugger detection");
            return Problem(
                detail: $"Error detecting debuggers: {ex.Message}",
                statusCode: Constants.Http.InternalServerError,
                title: "Debugger Detection Failed");
        }
    }

    /// <summary>
    /// Lists all available predefined analyses with descriptions
    /// </summary>
    /// <returns>Available analysis types and their descriptions</returns>
    [HttpGet("analyses")]
    [ProducesResponseType<AnalysesResponse>(StatusCodes.Status200OK)]
    public IActionResult GetAnalyses()
    {
        try
        {
            var analyses = _diagnosticsService.GetAvailableAnalyses();
            return Ok(new AnalysesResponse(analyses));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving analyses");
            return Problem(
                detail: $"Error retrieving analyses: {ex.Message}",
                statusCode: Constants.Http.InternalServerError,
                title: "Analysis Retrieval Failed");
        }
    }

}