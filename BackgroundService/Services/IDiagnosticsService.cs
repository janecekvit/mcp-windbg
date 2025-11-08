using Shared.Models;

namespace BackgroundService.Services;

public interface IDiagnosticsService
{
    /// <summary>
    /// Detects available CDB/WinDbg installations on the system
    /// </summary>
    /// <returns>Detected debugger paths and environment information</returns>
    DebuggerDetectionResponse DetectDebuggers();

    /// <summary>
    /// Gets all available predefined analyses with descriptions
    /// </summary>
    /// <returns>List of analysis information (name + description)</returns>
    IReadOnlyList<AnalysisInfo> GetAvailableAnalyses();
}
