using System.Diagnostics;
using System.Text;

namespace DumpAnalysisService.Infrastructure.Debugger;

/// <summary>
/// Infrastructure implementation for managing CDB debugger processes.
/// Handles process lifecycle, stdin/stdout communication, and process termination.
/// </summary>
public sealed class CdbProcessManager : ICdbProcessManager
{
    private const int DefaultReadBufferSize = 4096;
    private const int DefaultReadDelayMs = 10;
    private const int DefaultGracefulShutdownTimeoutMs = 5000;

    private readonly ILogger<CdbProcessManager> _logger;
    private readonly string _sessionId;
    private readonly object _lock = new();
    private Process? _cdbProcess;
    private StreamWriter? _stdin;
    private bool _disposed;

    public bool IsActive
    {
        get
        {
            lock (_lock)
            {
                return _IsActiveNoLock();
            }
        }
    }

    public int? ProcessId
    {
        get
        {
            lock (_lock)
            {
                return _cdbProcess?.Id;
            }
        }
    }

    public CdbProcessManager(string sessionId, ILogger<CdbProcessManager> logger)
    {
        _sessionId = sessionId;
        _logger = logger;
    }

    public Task<bool> StartProcessAsync(string cdbPath, string dumpFilePath, string symbolPath)
    {
        lock (_lock)
        {
            if (_IsActiveNoLock())
            {
                _logger.LogWarning("CDB process already running for session {SessionId}, terminating old process", _sessionId);
                _cdbProcess!.Kill();
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
        _EnsureProcessIsRunning();

        await _stdin!.WriteLineAsync(command);
        await _stdin.FlushAsync();
        _logger.LogTrace("Command written to CDB for session {SessionId}: {Command}", _sessionId, command);
    }

    public async Task<string> ReadUntilMarkerAsync(string endMarker, int timeoutMs, CancellationToken cancellationToken = default)
    {
        _EnsureProcessIsRunning();

        var output = new StringBuilder();
        var stdout = _cdbProcess!.StandardOutput;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeoutMs);

        try
        {
            await _ReadOutputUntilMarkerAsync(stdout, output, endMarker, cts.Token);

            var result = output.ToString();
            var markerIndex = result.IndexOf(endMarker, StringComparison.Ordinal);
            return result[..markerIndex].Trim();
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
        lock (_lock)
        {
            if (!_IsActiveNoLock())
                return;
        }

        try
        {
            _logger.LogWarning("Killing CDB process for session {SessionId}", _sessionId);

            lock (_lock)
            {
                _cdbProcess?.Kill(entireProcessTree: true);
            }

            if (_cdbProcess != null)
            {
                await _cdbProcess.WaitForExitAsync();
                _logger.LogInformation("CDB process killed for session {SessionId}", _sessionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error killing CDB process for session {SessionId}", _sessionId);
            throw;
        }
    }

    public async Task ShutdownGracefullyAsync(int timeoutMs = DefaultGracefulShutdownTimeoutMs)
    {
        lock (_lock)
        {
            if (!_IsActiveNoLock())
            {
                _logger.LogDebug("CDB process already exited for session {SessionId}", _sessionId);
                return;
            }
        }

        try
        {
            Process? process;
            lock (_lock)
            {
                if (_stdin != null)
                {
                    _stdin.WriteLineAsync("q").Wait();
                    _stdin.FlushAsync().Wait();
                }

                _logger.LogInformation("Sent graceful shutdown command to CDB for session {SessionId}", _sessionId);

                process = _cdbProcess;
            }


            if (process == null)
                return;

            var exited = process.WaitForExit(timeoutMs);
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
        if (_disposed)
            return;

        lock (_lock)
        {
            _stdin?.Dispose();

            if (_IsActiveNoLock())
            {
                try
                {
                    _cdbProcess!.Kill();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error killing CDB process during dispose for session {SessionId}", _sessionId);
                }
            }

            _cdbProcess?.Dispose();

            _disposed = true;
        }

        GC.SuppressFinalize(this);
    }


    private void _EnsureProcessIsRunning()
    {
        lock (_lock)
        {
            if (_stdin == null || !_IsActiveNoLock())
            {
                throw new InvalidOperationException($"CDB process not running for session {_sessionId}");
            }
        }
    }


    private bool _IsActiveNoLock()
    {
        return _cdbProcess?.HasExited == false;
    }

    private async Task _ReadOutputUntilMarkerAsync(StreamReader stdout, StringBuilder output, string endMarker, CancellationToken cancellationToken)
    {
        var buffer = new char[DefaultReadBufferSize];

        while (!cancellationToken.IsCancellationRequested)
        {
            var bytesRead = await stdout.ReadAsync(buffer, 0, buffer.Length).WaitAsync(cancellationToken);

            if (bytesRead > 0)
            {
                var chunk = new string(buffer, 0, bytesRead);
                output.Append(chunk);

                if (output.ToString().Contains(endMarker))
                {
                    return;
                }
            }

            await Task.Delay(DefaultReadDelayMs, cancellationToken);
        }

        throw new TimeoutException($"Timeout waiting for marker '{endMarker}' in session {_sessionId}");
    }
}
