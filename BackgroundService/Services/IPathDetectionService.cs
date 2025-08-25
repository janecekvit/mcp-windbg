namespace CdbBackgroundService.Services;

public interface IPathDetectionService
{
    (string? CdbPath, string? WinDbgPath, List<string> FoundPaths) DetectDebuggerPaths();
    string GetBestDebuggerPath();
    bool ValidateDebuggerPath(string path);
}