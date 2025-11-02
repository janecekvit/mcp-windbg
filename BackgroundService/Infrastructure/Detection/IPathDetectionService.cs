namespace BackgroundService.Infrastructure.Detection;

/// <summary>
/// Infrastructure service for detecting CDB installations on the system.
/// Searches standard Windows SDK installation paths and Windows Store app locations.
/// </summary>
public interface IPathDetectionService
{
    /// <summary>
    /// Detects available CDB installations on the system
    /// </summary>
    /// <returns>Tuple containing CDB path and list of all found debugger paths</returns>
    (string? CdbPath, List<string> FoundPaths) DetectDebuggerPaths();

    /// <summary>
    /// Gets the best available CDB debugger path
    /// </summary>
    /// <returns>Path to the CDB debugger executable</returns>
    string GetBestDebuggerPath();

    /// <summary>
    /// Validates if the provided path points to a valid CDB executable
    /// </summary>
    /// <param name="path">Path to validate</param>
    /// <returns>True if the path is a valid CDB debugger, false otherwise</returns>
    bool ValidateDebuggerPath(string path);
}
