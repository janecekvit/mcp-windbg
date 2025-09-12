namespace BackgroundService.Services;

public interface IAnalysisService
{
    /// <summary>
    /// Gets the list of WinDbg/CDB commands for a specific analysis type
    /// </summary>
    /// <param name="analysisName">Name of the analysis (e.g., 'basic', 'exception', 'threads')</param>
    /// <returns>List of debugger commands to execute</returns>
    IReadOnlyList<string> GetAnalysisCommands(string analysisName);
    
    /// <summary>
    /// Gets the list of all available predefined analysis types
    /// </summary>
    /// <returns>Enumerable of analysis names</returns>
    IEnumerable<string> GetAvailableAnalyses();
    
    /// <summary>
    /// Gets a human-readable description of what a specific analysis does
    /// </summary>
    /// <param name="analysisName">Name of the analysis to describe</param>
    /// <returns>Description text for the analysis</returns>
    string GetAnalysisDescription(string analysisName);
}