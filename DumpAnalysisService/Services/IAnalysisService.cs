using Shared.Models;

namespace DumpAnalysisService.Services;

public interface IAnalysisService
{
    /// <summary>
    /// Gets the list of WinDbg/CDB commands for a specific analysis type
    /// </summary>
    /// <param name="analysisType">Type of analysis to get commands for</param>
    /// <returns>List of debugger commands to execute</returns>
    IReadOnlyList<string> GetAnalysisCommands(AnalysisType analysisType);

    /// <summary>
    /// Gets the list of all available predefined analysis types
    /// </summary>
    /// <returns>Enumerable of analysis names</returns>
    IEnumerable<string> GetAvailableAnalyses();

    /// <summary>
    /// Gets a human-readable description of what a specific analysis does
    /// </summary>
    /// <param name="analysisType">Type of analysis to describe</param>
    /// <returns>Description text for the analysis</returns>
    string GetAnalysisDescription(AnalysisType analysisType);
}