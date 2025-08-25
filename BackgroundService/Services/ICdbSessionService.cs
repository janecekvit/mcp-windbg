namespace BackgroundService.Services;

public interface ICdbSessionService : IDisposable
{
    string SessionId { get; }
    string? CurrentDumpFile { get; }
    bool IsActive { get; }

    Task<bool> LoadDumpAsync(string dumpFilePath);
    Task<string> ExecuteCommandAsync(string command);
    Task<string> ExecuteBasicAnalysisAsync();
    Task<string> ExecutePredefinedAnalysisAsync(string analysisName);
}