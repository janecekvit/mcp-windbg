namespace BackgroundService.Infrastructure.Debugger;

/// <summary>
/// Infrastructure interface for managing CDB debugger processes.
/// Handles low-level process creation, communication, and lifetime management.
/// </summary>
public interface ICdbProcessManager : IDisposable
{
    /// <summary>
    /// Gets whether the CDB process is currently active
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Gets the process ID of the running CDB process, if any
    /// </summary>
    int? ProcessId { get; }

    /// <summary>
    /// Starts a new CDB process with the specified dump file and symbol path
    /// </summary>
    /// <param name="cdbPath">Path to cdb.exe</param>
    /// <param name="dumpFilePath">Path to the dump file to load</param>
    /// <param name="symbolPath">Constructed symbol path string</param>
    /// <returns>True if process started successfully</returns>
    Task<bool> StartProcessAsync(string cdbPath, string dumpFilePath, string symbolPath);

    /// <summary>
    /// Writes a command to the CDB process stdin
    /// </summary>
    /// <param name="command">Command to send</param>
    Task WriteCommandAsync(string command);

    /// <summary>
    /// Reads output from the CDB process until the specified marker is found
    /// </summary>
    /// <param name="endMarker">Marker string indicating end of output</param>
    /// <param name="timeoutMs">Timeout in milliseconds</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Output from CDB until marker is found</returns>
    Task<string> ReadUntilMarkerAsync(string endMarker, int timeoutMs, CancellationToken cancellationToken = default);

    /// <summary>
    /// Kills the CDB process forcefully
    /// </summary>
    Task KillProcessAsync();

    /// <summary>
    /// Sends quit command and waits for graceful shutdown
    /// </summary>
    /// <param name="timeoutMs">Timeout to wait for exit</param>
    Task ShutdownGracefullyAsync(int timeoutMs = 5000);
}
