using Shared.Models;

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
    /// <param name="progress">Structured progress reporter for long-running symbol loading</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    Task LoadDumpAsync(string dumpFilePath, IProgress<ProgressUpdate>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels the current session and terminates the CDB process
    /// </summary>
    Task CancelAsync();
    
    /// <summary>
    /// Executes a single WinDbg/CDB command in this session
    /// </summary>
    /// <param name="command">The debugger command to execute</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Output from the debugger command</returns>
    Task<string> ExecuteCommandAsync(string command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a single WinDbg/CDB command in this session with progress reporting
    /// </summary>
    /// <param name="command">The debugger command to execute</param>
    /// <param name="progress">Structured progress reporter for long-running commands</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Output from the debugger command</returns>
    Task<string> ExecuteCommandAsync(string command, IProgress<ProgressUpdate>? progress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a comprehensive basic analysis of the loaded dump
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Analysis output from the debugger</returns>
    Task<string> ExecuteBasicAnalysisAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a predefined analysis by name
    /// </summary>
    /// <param name="analysisName">Name of the analysis to run</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Analysis output from the debugger</returns>
    Task<string> ExecutePredefinedAnalysisAsync(string analysisName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a predefined analysis by name with progress reporting
    /// </summary>
    /// <param name="analysisName">Name of the analysis to run</param>
    /// <param name="progress">Structured progress reporter for long-running analysis</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>Analysis output from the debugger</returns>
    Task<string> ExecutePredefinedAnalysisAsync(string analysisName, IProgress<ProgressUpdate>? progress, CancellationToken cancellationToken = default);
}