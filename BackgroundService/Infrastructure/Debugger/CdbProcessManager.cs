using System.Diagnostics;
using System.Text;

namespace BackgroundService.Infrastructure.Debugger;

/// <summary>
/// Infrastructure implementation for managing CDB debugger processes.
/// Handles process lifecycle, stdin/stdout communication, and process termination.
/// </summary>
public sealed class CdbProcessManager : ICdbProcessManager
{
    private readonly ILogger<CdbProcessManager> _logger;
    private readonly string _sessionId;
    private Process? _cdbProcess;
    private StreamWriter? _stdin;
    private readonly object _lock = new();
    private bool _disposed;

    public bool IsActive => _cdbProcess?.HasExited == false;
    public int? ProcessId => _cdbProcess?.Id;

    public CdbProcessManager(string sessionId, ILogger<CdbProcessManager> logger)
    {
        _sessionId = sessionId;
        _logger = logger;
    }

    public Task<bool> StartProcessAsync(string cdbPath, string dumpFilePath, string symbolPath)
    {
        lock (_lock)
        {
            if (_cdbProcess != null && !_cdbProcess.HasExited)
            {
                _logger.LogWarning("CDB process already running for session {SessionId}, terminating old process", _sessionId);
                _cdbProcess.Kill();
                _cdbProcess.Dispose();
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = cdbPath,
                Arguments = $"-z \"{dumpFilePath}\" -y \"{symbolPath}\"",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            _logger.LogInformation("Starting CDB process for session {SessionId}: {CdbPath} {Arguments}",
                _sessionId, cdbPath, startInfo.Arguments);

            _cdbProcess = Process.Start(startInfo);

            if (_cdbProcess == null)
            {
                _logger.LogError("Failed to start CDB process for session {SessionId}", _sessionId);
                return Task.FromResult(false);
            }

            _logger.LogInformation("CDB process started for session {SessionId}, PID: {ProcessId}",
                _sessionId, _cdbProcess.Id);

            _stdin = _cdbProcess.StandardInput;

            // Check if process exited immediately
            if (_cdbProcess.HasExited)
            {
                var exitCode = _cdbProcess.ExitCode;
                _logger.LogError("CDB process exited immediately for session {SessionId} with exit code: {ExitCode}",
                    _sessionId, exitCode);
                return Task.FromResult(false);
            }

            return Task.FromResult(true);
        }
    }

    public async Task WriteCommandAsync(string command)
    {
        if (_stdin == null || _cdbProcess?.HasExited != false)
        {
            throw new InvalidOperationException($"CDB process not running for session {_sessionId}");
        }

        await _stdin.WriteLineAsync(command);
        await _stdin.FlushAsync();
        _logger.LogTrace("Command written to CDB for session {SessionId}: {Command}", _sessionId, command);
    }

    public async Task<string> ReadUntilMarkerAsync(string endMarker, int timeoutMs, CancellationToken cancellationToken = default)
    {
        if (_cdbProcess?.HasExited != false)
        {
            throw new InvalidOperationException($"CDB process not running for session {_sessionId}");
        }

        var output = new StringBuilder();
        var stdout = _cdbProcess.StandardOutput;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeoutMs);

        try
        {
            var buffer = new char[4096];
            while (!cts.Token.IsCancellationRequested)
            {
                // Read available data
                var readTask = stdout.ReadAsync(buffer, 0, buffer.Length);
                var bytesRead = await readTask.WaitAsync(cts.Token);

                if (bytesRead > 0)
                {
                    var chunk = new string(buffer, 0, bytesRead);
                    output.Append(chunk);

                    // Check if we've hit the end marker
                    if (output.ToString().Contains(endMarker))
                    {
                        var result = output.ToString();
                        var markerIndex = result.IndexOf(endMarker, StringComparison.Ordinal);
                        return result[..markerIndex].Trim();
                    }
                }

                // Small delay to avoid tight loop
                await Task.Delay(10, cts.Token);
            }

            throw new TimeoutException($"Timeout waiting for marker '{endMarker}' in session {_sessionId}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Read operation cancelled for session {SessionId}", _sessionId);
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"Timeout ({timeoutMs}ms) waiting for marker '{endMarker}' in session {_sessionId}");
        }
    }

    public async Task KillProcessAsync()
    {
        if (_cdbProcess != null && !_cdbProcess.HasExited)
        {
            try
            {
                _logger.LogWarning("Killing CDB process for session {SessionId}", _sessionId);
                _cdbProcess.Kill(entireProcessTree: true);
                await _cdbProcess.WaitForExitAsync();
                _logger.LogInformation("CDB process killed for session {SessionId}", _sessionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error killing CDB process for session {SessionId}", _sessionId);
                throw;
            }
        }
    }

    public async Task ShutdownGracefullyAsync(int timeoutMs = 5000)
    {
        if (_cdbProcess == null || _cdbProcess.HasExited)
        {
            _logger.LogDebug("CDB process already exited for session {SessionId}", _sessionId);
            return;
        }

        try
        {
            // Send quit command
            if (_stdin != null)
            {
                await _stdin.WriteLineAsync("q");
                await _stdin.FlushAsync();
            }

            // Wait for graceful exit
            var exited = _cdbProcess.WaitForExit(timeoutMs);
            if (!exited)
            {
                _logger.LogWarning("CDB process did not exit gracefully for session {SessionId}, forcing termination", _sessionId);
                await KillProcessAsync();
            }
            else
            {
                _logger.LogInformation("CDB process exited gracefully for session {SessionId}", _sessionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during graceful shutdown for session {SessionId}", _sessionId);
            await KillProcessAsync();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _stdin?.Dispose();

        if (_cdbProcess != null)
        {
            if (!_cdbProcess.HasExited)
            {
                try
                {
                    _cdbProcess.Kill();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error killing CDB process during dispose for session {SessionId}", _sessionId);
                }
            }

            _cdbProcess.Dispose();
        }

        _disposed = true;
    }
}
