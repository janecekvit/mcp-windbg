namespace BackgroundService.Services;

public interface IPathDetectionService
{
    /// <summary>
    /// Detects available CDB and WinDbg installations on the system
    /// </summary>
    /// <returns>Tuple containing CDB path, WinDbg path, and list of all found debugger paths</returns>
    (string? CdbPath, string? WinDbgPath, List<string> FoundPaths) DetectDebuggerPaths();
    
    /// <summary>
    /// Gets the best available debugger path, prioritizing CDB over WinDbg
    /// </summary>
    /// <returns>Path to the preferred debugger executable</returns>
    string GetBestDebuggerPath();
    
    /// <summary>
    /// Validates if the provided path points to a valid debugger executable
    /// </summary>
    /// <param name="path">Path to validate</param>
    /// <returns>True if the path is a valid debugger, false otherwise</returns>
    bool ValidateDebuggerPath(string path);
}