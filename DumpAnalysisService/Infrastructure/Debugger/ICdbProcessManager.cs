namespace DumpAnalysisService.Infrastructure.Debugger;

/// <summary>
/// Infrastructure abstraction for managing a CDB debugger process.
/// Handles process lifecycle and stdin/stdout communication.
/// </summary>
public interface ICdbProcessManager : IDisposable
{
    /// <summary>
    /// Gets whether the CDB process is currently running.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Gets the process ID of the running CDB process, or null if not running.
    /// </summary>
    int? ProcessId { get; }

    /// <summary>
    /// Starts a new CDB process for the given dump file.
    /// </summary>
    /// <param name="cdbPath">Path to cdb.exe</param>
    /// <param name="dumpFilePath">Path to the dump file to load</param>
    /// <param name="symbolPath">Symbol path to pass via -y</param>
    /// <returns>True if the process started successfully</returns>
    Task<bool> StartProcessAsync(string cdbPath, string dumpFilePath, string symbolPath);

    /// <summary>
    /// Sends one command line to CDB and returns its output.
    /// The caller must serialise calls; the command must be a single line.
    /// </summary>
    /// <param name="command">Command line to execute</param>
    /// <param name="timeoutMs">Timeout in milliseconds</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The command output without CDB prompts</returns>
    Task<string> ExecuteCommandAsync(string command, int timeoutMs, CancellationToken cancellationToken = default);

    /// <summary>
    /// Kills the CDB process immediately.
    /// </summary>
    Task KillProcessAsync();

    /// <summary>
    /// Attempts a graceful shutdown by sending "q", then kills the process after the timeout.
    /// </summary>
    /// <param name="timeoutMs">Timeout in milliseconds to wait for graceful exit</param>
    Task ShutdownGracefullyAsync(int timeoutMs = 5000);
}
