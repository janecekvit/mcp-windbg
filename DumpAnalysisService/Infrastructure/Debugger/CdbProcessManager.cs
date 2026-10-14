using System.Diagnostics;
using System.Text;
using System.Threading.Channels;

namespace DumpAnalysisService.Infrastructure.Debugger;

/// <summary>
/// Infrastructure implementation for managing CDB debugger processes.
/// Handles process lifecycle, stdin/stdout communication, and process termination.
/// stdout is drained by one background pump into a channel and stderr is drained to the log,
/// so a full pipe can never block cdb.exe and a timed-out read never leaves a pending read behind.
/// </summary>
public sealed class CdbProcessManager : ICdbProcessManager
{
    private const int DefaultReadBufferSize = 4096;
    private const int DefaultGracefulShutdownTimeoutMs = 5000;

    private readonly ILogger<CdbProcessManager> _logger;
    private readonly string _sessionId;
    private readonly object _lock = new();
    private Process? _cdbProcess;
    private StreamWriter? _stdin;
    private Channel<string>? _stdoutChannel;
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

    /// <summary>
    /// Builds the cdb.exe start info. Arguments go through <see cref="ProcessStartInfo.ArgumentList"/>,
    /// which quotes each one, so no path or symbol path value can add arguments of its own.
    /// </summary>
    public static ProcessStartInfo CreateStartInfo(string cdbPath, string dumpFilePath, string symbolPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = cdbPath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        // -noshell: disables .shell for the whole session (defence in depth behind CdbCommandPolicy).
        startInfo.ArgumentList.Add("-noshell");
        startInfo.ArgumentList.Add("-z");
        startInfo.ArgumentList.Add(dumpFilePath);
        startInfo.ArgumentList.Add("-y");
        startInfo.ArgumentList.Add(symbolPath);

        return startInfo;
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

            var startInfo = CreateStartInfo(cdbPath, dumpFilePath, symbolPath);

            _logger.LogInformation("Starting CDB process for session {SessionId}: {CdbPath} {Arguments}",
                _sessionId, cdbPath, string.Join(" ", startInfo.ArgumentList));

            _cdbProcess = Process.Start(startInfo);

            if (_cdbProcess == null)
            {
                _logger.LogError("Failed to start CDB process for session {SessionId}", _sessionId);
                return Task.FromResult(false);
            }

            _logger.LogInformation("CDB process started for session {SessionId}, PID: {ProcessId}",
                _sessionId, _cdbProcess.Id);

            _stdin = _cdbProcess.StandardInput;

            _cdbProcess.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    _logger.LogDebug("CDB stderr for session {SessionId}: {Line}", _sessionId, e.Data);
            };
            _cdbProcess.BeginErrorReadLine();

            _stdoutChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true
            });
            _ = _PumpStdoutAsync(_cdbProcess.StandardOutput, _stdoutChannel.Writer);

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

    public async Task<string> ExecuteCommandAsync(string command, int timeoutMs, CancellationToken cancellationToken = default)
    {
        _EnsureProcessIsRunning();

        var id = Guid.NewGuid().ToString("N");
        var collector = new CdbOutputCollector($"__START_COMMAND_{id}__", $"__END_COMMAND_{id}__");

        // Three separate input lines: whatever the command does to the rest of its own line
        // (a '*' comment, an unbalanced quote) cannot affect the markers.
        await _stdin!.WriteLineAsync($".echo __START_COMMAND_{id}__");
        await _stdin.WriteLineAsync(command);
        await _stdin.WriteLineAsync($".echo __END_COMMAND_{id}__");
        await _stdin.FlushAsync(cancellationToken);
        _logger.LogTrace("Command written to CDB for session {SessionId}: {Command}", _sessionId, command);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeoutMs);

        try
        {
            var reader = _stdoutChannel!.Reader;
            while (await reader.WaitToReadAsync(cts.Token))
            {
                while (reader.TryRead(out var chunk))
                {
                    if (collector.Append(chunk))
                        return collector.Result;
                }
            }

            throw new InvalidOperationException($"CDB process exited while executing a command in session {_sessionId}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Read operation cancelled for session {SessionId}", _sessionId);
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"Timeout ({timeoutMs}ms) waiting for command output in session {_sessionId}");
        }
    }

    public async Task KillProcessAsync()
    {
        Process? process;
        lock (_lock)
        {
            if (!_IsActiveNoLock())
                return;
            process = _cdbProcess;
        }

        try
        {
            _logger.LogWarning("Killing CDB process for session {SessionId}", _sessionId);
            process!.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            _logger.LogInformation("CDB process killed for session {SessionId}", _sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error killing CDB process for session {SessionId}", _sessionId);
            throw;
        }
    }

    public async Task ShutdownGracefullyAsync(int timeoutMs = DefaultGracefulShutdownTimeoutMs)
    {
        Process? process;
        StreamWriter? stdin;
        lock (_lock)
        {
            if (!_IsActiveNoLock())
            {
                _logger.LogDebug("CDB process already exited for session {SessionId}", _sessionId);
                return;
            }

            process = _cdbProcess;
            stdin = _stdin;
        }

        try
        {
            if (stdin != null)
            {
                await stdin.WriteLineAsync("q");
                await stdin.FlushAsync();
            }

            _logger.LogInformation("Sent graceful shutdown command to CDB for session {SessionId}", _sessionId);

            using var cts = new CancellationTokenSource(timeoutMs);
            try
            {
                await process!.WaitForExitAsync(cts.Token);
                _logger.LogInformation("CDB process exited gracefully for session {SessionId}", _sessionId);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("CDB process did not exit gracefully for session {SessionId}, forcing termination", _sessionId);
                await KillProcessAsync();
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
                    _cdbProcess!.Kill(entireProcessTree: true);
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
            if (_stdin == null || _stdoutChannel == null || !_IsActiveNoLock())
            {
                throw new InvalidOperationException($"CDB process not running for session {_sessionId}");
            }
        }
    }

    private bool _IsActiveNoLock()
    {
        return _cdbProcess?.HasExited == false;
    }

    private async Task _PumpStdoutAsync(StreamReader stdout, ChannelWriter<string> writer)
    {
        var buffer = new char[DefaultReadBufferSize];
        try
        {
            int read;
            while ((read = await stdout.ReadAsync(buffer, 0, buffer.Length)) > 0)
                writer.TryWrite(new string(buffer, 0, read));
        }
        catch (Exception ex) when (ex is ObjectDisposedException or IOException or InvalidOperationException)
        {
            _logger.LogDebug(ex, "CDB stdout closed for session {SessionId}", _sessionId);
        }
        finally
        {
            writer.TryComplete();
        }
    }
}
