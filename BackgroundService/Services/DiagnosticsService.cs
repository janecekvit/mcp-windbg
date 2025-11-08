using BackgroundService.Infrastructure.Detection;
using Shared.Models;

namespace BackgroundService.Services;

/// <summary>
/// Business service for diagnostic operations (debugger detection, analysis listing).
/// Acts as a layer between controllers and infrastructure services.
/// </summary>
public class DiagnosticsService : IDiagnosticsService
{
    private readonly ILogger<DiagnosticsService> _logger;
    private readonly IPathDetectionService _pathDetectionService;
    private readonly IAnalysisService _analysisService;

    public DiagnosticsService(
        ILogger<DiagnosticsService> logger,
        IPathDetectionService pathDetectionService,
        IAnalysisService analysisService)
    {
        _logger = logger;
        _pathDetectionService = pathDetectionService;
        _analysisService = analysisService;
    }

    public DebuggerDetectionResponse DetectDebuggers()
    {
        try
        {
            var foundPaths = _pathDetectionService.DetectDebuggerPaths();
            var cdbPath = _pathDetectionService.GetBestDebuggerPath();

            _logger.LogInformation("Debugger detection completed via DiagnosticsService. CDB: {CdbPath}", cdbPath);

            return new DebuggerDetectionResponse(cdbPath, foundPaths);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in debugger detection");
            throw;
        }
    }

    public IReadOnlyList<AnalysisInfo> GetAvailableAnalyses()
    {
        try
        {
            var analyses = _analysisService.GetAvailableAnalyses()
                .Select(a => new AnalysisInfo(a, _analysisService.GetAnalysisDescription(a.ToAnalysisType())))
                .ToList();

            _logger.LogInformation("Retrieved {Count} available analyses via DiagnosticsService", analyses.Count);

            return analyses;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving available analyses");
            throw;
        }
    }
}
