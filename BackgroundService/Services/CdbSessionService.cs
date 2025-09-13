using System.Diagnostics;
using System.Text;
using Common;

namespace BackgroundService.Services;

public sealed class CdbSessionService : ICdbSessionService
{
    private readonly ILogger<CdbSessionService> _logger;
    private readonly IAnalysisService _analysisService;
    private readonly string _cdbPath;
    private readonly string _symbolCache;
    private readonly string _symbolPathExtra;
    private Process? _cdbProcess;
    private StreamWriter? _stdin;
    private bool _isInitialized;
    private readonly object _lock = new();
    private readonly SemaphoreSlim _commandSemaphore = new(1, 1);

    public string SessionId { get; }
    public string? CurrentDumpFile { get; private set; }
    public bool IsActive => _cdbProcess?.HasExited == false;

    public CdbSessionService(string sessionId, ILogger<CdbSessionService> logger,
                            IAnalysisService analysisService,
                            string cdbPath = @"C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\cdb.exe",
                            string symbolCache = @"C:\symbols",
                            string symbolPathExtra = "")
    {
        SessionId = sessionId;
        _logger = logger;
        _analysisService = analysisService;
        _cdbPath = cdbPath;
        _symbolCache = symbolCache;
        _symbolPathExtra = symbolPathExtra;
    }

    public async Task LoadDumpAsync(string dumpFilePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(dumpFilePath))
        {
            _logger.LogError("Dump file not found: {DumpFile}", dumpFilePath);
            throw new FileNotFoundException($"Dump file not found: {dumpFilePath}", dumpFilePath);
        }

        if (!File.Exists(_cdbPath))
        {
            _logger.LogError("CDB not found at: {CdbPath}", _cdbPath);
            throw new FileNotFoundException($"CDB not found at: {_cdbPath}", _cdbPath);
        }

        lock (_lock)
        {
            // Close existing session if running
            if (_cdbProcess != null && !_cdbProcess.HasExited)
            {
                _cdbProcess.Kill();
                _cdbProcess.Dispose();
            }

            CurrentDumpFile = dumpFilePath;

            // Prepare symbol cache
            Directory.CreateDirectory(_symbolCache);

            // Build symbol path
            var msSrv = $"srv*{_symbolCache}*https://msdl.microsoft.com/download/symbols";
            var symbolPath = string.IsNullOrWhiteSpace(_symbolPathExtra) ? msSrv : $"{_symbolPathExtra};{msSrv}";

            // Start CDB process
            var startInfo = new ProcessStartInfo
            {
                FileName = _cdbPath,
                Arguments = $"-z \"{dumpFilePath}\" -y \"{symbolPath}\"",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            _logger.LogInformation("Starting CDB process for session {SessionId}: {CdbPath} {Arguments}", SessionId, _cdbPath, startInfo.Arguments);
            _cdbProcess = Process.Start(startInfo);

            if (_cdbProcess == null)
            {
                _logger.LogError("Failed to start CDB process for session {SessionId}", SessionId);
                throw new InvalidOperationException("Failed to start CDB process");
            }

            _logger.LogInformation("CDB process started for session {SessionId}, PID: {ProcessId}", SessionId, _cdbProcess.Id);
            _stdin = _cdbProcess.StandardInput;
            _isInitialized = false;

            // Check if process exited immediately
            if (_cdbProcess.HasExited)
            {
                var exitCode = _cdbProcess.ExitCode;
                _logger.LogError("CDB process exited immediately for session {SessionId} with exit code: {ExitCode}", SessionId, exitCode);
                throw new InvalidOperationException($"CDB process exited immediately with exit code: {exitCode}");
            }
        }

        // Wait for initialization and set symbols
        await InitializeSessionAsync(cancellationToken);

        _logger.LogInformation("CDB session {SessionId} loaded dump: {DumpFile}", SessionId, dumpFilePath);
    }

    private async Task InitializeSessionAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized) return;

        _logger.LogInformation("Initializing CDB session {SessionId}", SessionId);

        var initCommands = new List<string>
        {
            $".symfix {_symbolCache}",
            ".symopt+ 0x40",
            ".reload",
            ".echo Session initialized successfully"
        };

        foreach (var command in initCommands)
        {
            _logger.LogDebug("Executing init command for session {SessionId}: {Command}", SessionId, command);
            var result = await ExecuteCommandInternalAsync(command, cancellationToken);
            _logger.LogDebug("Init command result for session {SessionId}: {Result}", SessionId, result?.Length > 100 ? result[..100] + "..." : result);
            await Task.Delay(500, cancellationToken); // Short pause between commands
        }

        _isInitialized = true;
        _logger.LogInformation("CDB session {SessionId} initialized successfully", SessionId);
    }

    public async Task<string> ExecuteCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        if (_cdbProcess?.HasExited != false)
        {
            var error = "CDB process is not running. Load a dump file first.";
            _logger.LogError(error);
            throw new InvalidOperationException(error);
        }

        if (!_isInitialized)
        {
            var error = "Session not initialized. Load a dump file first.";
            _logger.LogError(error);
            throw new InvalidOperationException(error);
        }

        await _commandSemaphore.WaitAsync();
        try
        {
            return await ExecuteCommandInternalAsync(command, cancellationToken);
        }
        finally
        {
            _commandSemaphore.Release();
        }
    }

    private async Task<string> ExecuteCommandInternalAsync(string command, CancellationToken cancellationToken = default)
    {
        if (_stdin == null || _cdbProcess?.HasExited != false)
        {
            var error = "CDB process is not available";
            _logger.LogError(error);
            throw new InvalidOperationException(error);
        }

        try
        {
            // Use unique marker to identify end of output
            var marker = $"__END_COMMAND_{Guid.NewGuid():N}__";
            var fullCommand = $"{command}; .echo {marker}";

            lock (_lock)
            {
                _stdin.WriteLine(fullCommand);
                _stdin.Flush();
            }

            // Read output until we find the marker - without Task.Run to avoid concurrent stream access
            var reader = _cdbProcess.StandardOutput;
            var buffer = new char[4096];
            var result = new StringBuilder();
            
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(2)); // Increased timeout
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            while (!_cdbProcess.HasExited && !combinedCts.Token.IsCancellationRequested)
            {
                try
                {
                    var readTask = reader.ReadAsync(buffer, 0, buffer.Length);
                    var bytesRead = await readTask.ConfigureAwait(false);
                    
                    if (bytesRead == 0) 
                        break;

                    var chunk = buffer.AsSpan(0, bytesRead).ToString();
                    result.Append(chunk);

                    // Check for marker
                    var currentOutput = result.ToString();
                    if (currentOutput.Contains(marker))
                    {
                        var markerIndex = currentOutput.LastIndexOf(marker);
                        return currentOutput[..markerIndex].Trim();
                    }
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogInformation("Command execution cancelled for: {Command}", command);
                        throw;
                    }
                    _logger.LogError("Command execution timeout for: {Command}", command);
                    throw new TimeoutException("Command execution timeout");
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("currently in use"))
                {
                    // Stream is busy, wait a bit and retry
                    await Task.Delay(50, combinedCts.Token).ConfigureAwait(false);
                    continue;
                }
            }

            if (combinedCts.Token.IsCancellationRequested)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Command execution cancelled for: {Command}", command);
                    throw new OperationCanceledException("Command execution was cancelled", cancellationToken);
                }
                _logger.LogError("Command execution timeout for: {Command}", command);
                throw new TimeoutException("Command execution timeout");
            }

            return result.ToString();
        }
        catch (Exception ex) when (!(ex is TimeoutException || ex is InvalidOperationException))
        {
            _logger.LogError(ex, "Error executing command: {Command}", command);
            throw new InvalidOperationException($"Error executing command: {ex.Message}", ex);
        }
    }

    public async Task<string> ExecuteBasicAnalysisAsync(CancellationToken cancellationToken = default)
    {
        return await ExecutePredefinedAnalysisAsync("basic", cancellationToken);
    }

    public async Task<string> ExecutePredefinedAnalysisAsync(string analysisName, CancellationToken cancellationToken = default)
    {
        var commands = _analysisService.GetAnalysisCommands(analysisName);
        if (commands.Count == 0)
        {
            var error = $"Unknown analysis type: {analysisName}. Available analyses: {string.Join(", ", _analysisService.GetAvailableAnalyses())}";
            _logger.LogError(error);
            throw new ArgumentException(error, nameof(analysisName));
        }

        var results = new StringBuilder()
            .AppendSection($"Executing {analysisName} analysis:")
            .AppendKeyValue("Description", _analysisService.GetAnalysisDescription(analysisName));

        foreach (var command in commands)
        {
            var result = await ExecuteCommandAsync(command, cancellationToken);
            results.AppendLine(result);
            results.AppendLine();
        }

        return results.ToString();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            try
            {
                if (_cdbProcess?.HasExited == false)
                {
                    _stdin?.WriteLine("q");
                    _stdin?.Flush();

                    if (!_cdbProcess.WaitForExit(5000))
                        _cdbProcess.Kill();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing CDB session {SessionId}", SessionId);
            }
            finally
            {
                _stdin?.Dispose();
                _cdbProcess?.Dispose();
                _commandSemaphore.Dispose();
            }
        }
        GC.SuppressFinalize(this);
    }
}