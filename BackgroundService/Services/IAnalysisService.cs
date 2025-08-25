namespace CdbBackgroundService.Services;

public interface IAnalysisService
{
    string[] GetAnalysisCommands(string analysisName);
    IEnumerable<string> GetAvailableAnalyses();
    string GetAnalysisDescription(string analysisName);
}