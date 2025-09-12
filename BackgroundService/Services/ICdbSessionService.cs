namespace BackgroundService.Services;

public interface ICdbSessionService : IDisposable
{
    /// <summary>
    /// Gets the unique identifier for this debugging session
    /// </summary>
    string SessionId { get; }
    
    /// <summary>
    /// Gets the path to the currently loaded dump file, if any
    /// </summary>
    string? CurrentDumpFile { get; }
    
    /// <summary>
    /// Gets whether this debugging session is currently active
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Loads a memory dump file into this debugging session
    /// </summary>
    /// <param name="dumpFilePath">Path to the dump file to load</param>
    Task LoadDumpAsync(string dumpFilePath);
    
    /// <summary>
    /// Executes a single WinDbg/CDB command in this session
    /// </summary>
    /// <param name="command">The debugger command to execute</param>
    /// <returns>Output from the debugger command</returns>
    Task<string> ExecuteCommandAsync(string command);
    
    /// <summary>
    /// Executes a comprehensive basic analysis of the loaded dump
    /// </summary>
    /// <returns>Analysis output from the debugger</returns>
    Task<string> ExecuteBasicAnalysisAsync();
    
    /// <summary>
    /// Executes a predefined analysis by name
    /// </summary>
    /// <param name="analysisName">Name of the analysis to run</param>
    /// <returns>Analysis output from the debugger</returns>
    Task<string> ExecutePredefinedAnalysisAsync(string analysisName);
}