using System.Reflection;
using BackgroundService.Services;
using Common;
using Microsoft.AspNetCore.Mvc;

namespace BackgroundService.Controllers;

[ApiController]
[Route("api/diagnostics")]
[Produces("application/json")]
public class DiagnosticsController : ControllerBase
{
    private readonly ILogger<DiagnosticsController> _logger;
    private readonly IPathDetectionService _pathDetectionService;
    private readonly IAnalysisService _analysisService;

    public DiagnosticsController(
        ILogger<DiagnosticsController> logger,
        IPathDetectionService pathDetectionService,
        IAnalysisService analysisService)
    {
        _logger = logger;
        _pathDetectionService = pathDetectionService;
        _analysisService = analysisService;
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
            var (cdbPath, winDbgPath, foundPaths) = _pathDetectionService.DetectDebuggerPaths();

            var envVars = new Dictionary<string, string?>
            {
                ["CDB_PATH"] = Environment.GetEnvironmentVariable("CDB_PATH"),
                ["SYMBOL_CACHE"] = Environment.GetEnvironmentVariable("SYMBOL_CACHE"),
                ["SYMBOL_PATH_EXTRA"] = Environment.GetEnvironmentVariable("SYMBOL_PATH_EXTRA")
            };

            _logger.LogInformation("Debugger detection completed. CDB: {CdbPath}, WinDbg: {WinDbgPath}", cdbPath, winDbgPath);

            return Ok(new DebuggerDetectionResponse(cdbPath, winDbgPath, foundPaths, envVars));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in enhanced debugger detection");
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
            var analyses = _analysisService.GetAvailableAnalyses()
                .Select(a => new AnalysisInfo(a, _analysisService.GetAnalysisDescription(a)))
                .ToList();

            _logger.LogInformation("Retrieved {Count} available analyses", analyses.Count);

            return Ok(new AnalysesResponse(analyses));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving enhanced analyses");
            return Problem(
                detail: $"Error retrieving analyses: {ex.Message}",
                statusCode: Constants.Http.InternalServerError,
                title: "Analysis Retrieval Failed");
        }
    }

}