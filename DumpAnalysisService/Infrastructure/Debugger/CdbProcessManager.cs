using System.Collections;
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

    /// <summary>
    /// Environment variables cdb.exe inherits from the service: what Windows, dbghelp and symsrv
    /// need (system and profile directories, temp, executable search path, processor details).
    /// Everything else (the service's own settings, credentials, _NT_* debugger variables) is not
    /// passed; Debugger:CdbEnvironmentVariables adds names.
    /// </summary>
    public static readonly IReadOnlyList<string> InheritedEnvironmentVariables =
    [
        "SystemRoot", "windir", "SystemDrive", "TEMP", "TMP", "USERPROFILE", "LOCALAPPDATA", "APPDATA",
        "HOMEDRIVE", "HOMEPATH", "ALLUSERSPROFILE", "ProgramData", "ProgramFiles", "ProgramFiles(x86)",
        "ProgramW6432", "CommonProgramFiles", "CommonProgramFiles(x86)", "CommonProgramW6432",
        "PATH", "PATHEXT", "NUMBER_OF_PROCESSORS", "OS", "COMPUTERNAME", "USERNAME", "USERDOMAIN",
    ];

    private const string ProcessorVariablePrefix = "PROCESSOR_";

    private readonly ILogger<CdbProcessManager> _logger;
    private readonly string _sessionId;
    private readonly IReadOnlyCollection<string> _extraEnvironmentVariables;
    private readonly bool _restrictChildProcesses;
    private readonly object _lock = new();
    private Process? _cdbProcess;
    private StreamWriter? _stdin;
    private Channel<string>? _stdoutChannel;
    private IDisposable? _jobObject;
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

    /// <param name="sessionId">Session the process belongs to (for the log).</param>
    /// <param name="logger">Logger.</param>
    /// <param name="extraEnvironmentVariables">Debugger:CdbEnvironmentVariables.</param>
    /// <param name="restrictChildProcesses">Debugger:RestrictCdbChildProcesses (Windows job object limit).</param>
    public CdbProcessManager(
        string sessionId,
        ILogger<CdbProcessManager> logger,
        IReadOnlyCollection<string>? extraEnvironmentVariables = null,
        bool restrictChildProcesses = true)
    {
        _sessionId = sessionId;
        _logger = logger;
        _extraEnvironmentVariables = extraEnvironmentVariables ?? [];
        _restrictChildProcesses = restrictChildProcesses;
    }

    /// <summary>
    /// Builds the cdb.exe start info. Arguments go through <see cref="ProcessStartInfo.ArgumentList"/>,
    /// which quotes each one, so no path or symbol path value can add arguments of its own.
    /// The environment is rebuilt from <see cref="InheritedEnvironmentVariables"/> plus
    /// <paramref name="extraEnvironmentVariables"/>; the symbol path comes only from -y.
    /// </summary>
    /// <param name="sourceEnvironment">The environment to copy from (tests); defaults to the service's own.</param>
    public static ProcessStartInfo CreateStartInfo(
        string cdbPath,
        string dumpFilePath,
        string symbolPath,
        IEnumerable<string>? extraEnvironmentVariables = null,
        IDictionary? sourceEnvironment = null)
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

        _SetMinimalEnvironment(startInfo, extraEnvironmentVariables, sourceEnvironment ?? Environment.GetEnvironmentVariables());

        return startInfo;
    }

    private static void _SetMinimalEnvironment(ProcessStartInfo startInfo, IEnumerable<string>? extra, IDictionary source)
    {
        var allowed = new HashSet<string>(InheritedEnvironmentVariables, StringComparer.OrdinalIgnoreCase);
        foreach (var name in extra ?? [])
        {
            if (!string.IsNullOrWhiteSpace(name))
                allowed.Add(name.Trim());
        }

        startInfo.Environment.Clear();
        foreach (DictionaryEntry entry in source)
        {
            if (entry.Key is not string name || entry.Value is not string value)
                continue;

            if (allowed.Contains(name) || name.StartsWith(ProcessorVariablePrefix, StringComparison.OrdinalIgnoreCase))
                startInfo.Environment[name] = value;
        }
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

            var startInfo = CreateStartInfo(cdbPath, dumpFilePath, symbolPath, _extraEnvironmentVariables);

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

            // Before the first command is sent: the job ends cdb.exe with the service and,
            // unless turned off, allows it no child processes.
            if (OperatingSystem.IsWindows())
            {
                _jobObject?.Dispose();
                var jobObject = new CdbJobObject(allowChildProcesses: !_restrictChildProcesses);
                try
                {
                    jobObject.Assign(_cdbProcess);
                }
                catch
                {
                    jobObject.Dispose();
                    _cdbProcess.Kill();
                    throw;
                }
                _jobObject = jobObject;
            }

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
            _jobObject?.Dispose();

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
