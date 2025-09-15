using System.Diagnostics;
using System.Text;
using Shared;
using Shared.Extensions;

namespace BackgroundService.Services;

public sealed class CdbSessionService : ICdbSessionService
{
    private readonly ILogger<CdbSessionService> _logger;
    private readonly IAnalysisService _analysisService;
    private readonly string _cdbPath;
    private readonly string _symbolCache;
    private readonly string _symbolPathExtra;
    private readonly string? _symbolServers;
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
                            string symbolPathExtra = "",
                            string? symbolServers = null)
    {
        SessionId = sessionId;
        _logger = logger;
        _analysisService = analysisService;
        _cdbPath = cdbPath;
        _symbolCache = symbolCache;
        _symbolPathExtra = symbolPathExtra;
        _symbolServers = symbolServers;
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

            // Build comprehensive symbol path
            var localSymbols = _symbolCache;
            var symbolPathParts = new List<string>();

            // Add extra symbol paths first (highest priority)
            if (!string.IsNullOrWhiteSpace(_symbolPathExtra))
            {
                symbolPathParts.AddRange(_symbolPathExtra.Split(';', StringSplitOptions.RemoveEmptyEntries));
            }

            // Add custom symbol servers if specified
            if (!string.IsNullOrWhiteSpace(_symbolServers))
            {
                foreach (var server in _symbolServers.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmedServer = server.Trim();
                    if (trimmedServer.StartsWith("http://") || trimmedServer.StartsWith("https://"))
                    {
                        // It's a URL - add as srv*cache*url
                        symbolPathParts.Add($"srv*{localSymbols}*{trimmedServer}");
                    }
                    else
                    {
                        // It's a file path - add directly
                        symbolPathParts.Add(trimmedServer);
                    }
                }
                _logger.LogInformation("Using custom symbol servers for session {SessionId}: {SymbolServers}", SessionId, _symbolServers);
            }

            // Add default Microsoft symbol servers (lower priority)
            var defaultServers = new[]
            {
                $"srv*{localSymbols}*https://msdl.microsoft.com/download/symbols",
                $"srv*{localSymbols}*https://symbols.nuget.org/download/symbols",
                $"srv*{localSymbols}*https://download.microsoft.com/download/symbols"
            };
            symbolPathParts.AddRange(defaultServers);

            var symbolPath = string.Join(";", symbolPathParts.Where(p => !string.IsNullOrWhiteSpace(p)));

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
            // Set symbol options for better debugging
            ".symopt+ 0x40",          // SYMOPT_DEFERRED_LOADS
            ".symopt+ 0x400",         // SYMOPT_NO_PROMPTS
            ".symopt+ 0x800",         // SYMOPT_FAIL_CRITICAL_ERRORS
            ".symopt- 0x2",           // SYMOPT_UNDNAME (disable for cleaner output)

            // Force symbol path setup - add custom servers first if available
        };

        // Add custom symbol servers to init commands
        if (!string.IsNullOrWhiteSpace(_symbolServers))
        {
            foreach (var server in _symbolServers.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmedServer = server.Trim();
                if (trimmedServer.StartsWith("http://") || trimmedServer.StartsWith("https://"))
                {
                    initCommands.Add($".sympath+ srv*{_symbolCache}*{trimmedServer}");
                }
                else
                {
                    initCommands.Add($".sympath+ {trimmedServer}");
                }
            }
        }

        // Add default Microsoft symbol servers
        initCommands.AddRange(new[]
        {
            $".sympath+ srv*{_symbolCache}*https://msdl.microsoft.com/download/symbols",
            $".sympath+ srv*{_symbolCache}*https://symbols.nuget.org/download/symbols",

            // Reload symbols with enhanced output - with retry logic
            ".reload /f",

            // Wait for symbol loading to complete
            ".echo Waiting for symbol loading to complete...",

            // Verify symbol loading with retry
            ".echo === Symbol Loading Status ===",
            "lm",
            ".echo === Session initialized successfully ===",
            ".echo"
        });

        foreach (var command in initCommands)
        {
            _logger.LogDebug("Executing init command for session {SessionId}: {Command}", SessionId, command);
            var result = await ExecuteCommandInternalAsync(command, null, cancellationToken);

            // Check for symbol loading issues and retry if needed
            if (command == ".reload /f" && (result.Contains("WRONG_SYMBOLS") || result.Contains("MISSING")))
            {
                _logger.LogWarning("Symbol loading issues detected, attempting retry for session {SessionId}", SessionId);
                await RetrySymbolLoadingAsync(cancellationToken);
            }

            _logger.LogDebug("Init command result for session {SessionId}: {Result}", SessionId, result?.Length > Constants.Debugging.LogTruncateLength ? result[..Constants.Debugging.LogTruncateLength] + "..." : result);
            await Task.Delay(Constants.Debugging.InitializationDelay, cancellationToken); // Short pause between commands
        }

        _isInitialized = true;
        _logger.LogInformation("CDB session {SessionId} initialized successfully", SessionId);
    }

    private async Task RetrySymbolLoadingAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Retrying symbol loading for session {SessionId}", SessionId);

        var retryCommands = new[]
        {
            // Clear symbol cache and try alternative servers
            ".symfix",
            ".reload /f",
            ".echo Retrying with alternative symbol servers...",

            // Try loading with different approach
            $".sympath srv*{_symbolCache}*https://msdl.microsoft.com/download/symbols",
            ".reload /v",  // Verbose reload to see what's happening

            // Alternative: Load PDB specifically if available
            ".echo === Symbol Retry Status ===",
            "lm v",  // Verbose module list to check symbols
        };

        foreach (var command in retryCommands)
        {
            try
            {
                _logger.LogDebug("Executing retry command for session {SessionId}: {Command}", SessionId, command);
                var result = await ExecuteCommandInternalAsync(command, null, cancellationToken);
                _logger.LogDebug("Retry command result for session {SessionId}: {Result}", SessionId, result?.Length > Constants.Debugging.LogTruncateLength ? result[..Constants.Debugging.LogTruncateLength] + "..." : result);
                await Task.Delay(Constants.Debugging.InitializationDelay * 2, cancellationToken); // Longer pause for retries
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Retry command failed for session {SessionId}: {Command}", SessionId, command);
                // Continue with next retry command
            }
        }
    }

    public async Task<string> ExecuteCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        return await ExecuteCommandAsync(command, null, cancellationToken);
    }

    public async Task<string> ExecuteCommandAsync(string command, IProgress<string>? progress, CancellationToken cancellationToken = default)
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

        await _commandSemaphore.WaitAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Executing command in session {SessionId}: {Command}", SessionId, command);
            progress?.Report($"Executing command: {command}");
            return await ExecuteCommandInternalAsync(command, progress, cancellationToken);
        }
        finally
        {
            _commandSemaphore.Release();
        }
    }

    private async Task<string> ExecuteCommandInternalAsync(string command, IProgress<string>? progress, CancellationToken cancellationToken = default)
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
            var buffer = new char[Constants.Debugging.ReadBufferSize];
            var result = new StringBuilder();

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(5)); // Increased timeout for symbol loading
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var lastProgressUpdate = DateTime.UtcNow;
            var progressInterval = TimeSpan.FromSeconds(2);

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

                    // Progress reporting for long-running commands
                    if (progress != null && DateTime.UtcNow - lastProgressUpdate > progressInterval)
                    {
                        var outputLength = result.Length;
                        var elapsed = DateTime.UtcNow - lastProgressUpdate;
                        progress.Report($"Command '{command}' running... ({outputLength} chars received, {elapsed:mm\\:ss} elapsed)");
                        lastProgressUpdate = DateTime.UtcNow;
                    }

                    // Check for marker
                    var currentOutput = result.ToString();
                    if (currentOutput.Contains(marker))
                    {
                        var markerIndex = currentOutput.LastIndexOf(marker);
                        progress?.Report($"Command '{command}' completed successfully");
                        return currentOutput[..markerIndex].Trim();
                    }
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogInformation("Command execution cancelled by user for: {Command}", command);
                        progress?.Report($"Command '{command}' was cancelled by user");
                        throw new OperationCanceledException("Command execution was cancelled by user", cancellationToken);
                    }
                    _logger.LogWarning("Command execution timeout for: {Command} (exceeded 5 minutes)", command);
                    progress?.Report($"Command '{command}' timed out after 5 minutes");
                    throw new TimeoutException($"Command '{command}' execution timeout (exceeded 5 minutes)");
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("currently in use"))
                {
                    // Stream is busy, wait a bit and retry
                    await Task.Delay(Constants.Debugging.PollingDelay, combinedCts.Token).ConfigureAwait(false);
                    continue;
                }
            }

            if (combinedCts.Token.IsCancellationRequested)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Command execution cancelled by user for: {Command}", command);
                    progress?.Report($"Command '{command}' was cancelled by user");
                    throw new OperationCanceledException("Command execution was cancelled by user", cancellationToken);
                }
                _logger.LogWarning("Command execution timeout for: {Command} (exceeded 5 minutes)", command);
                progress?.Report($"Command '{command}' timed out after 5 minutes");
                throw new TimeoutException($"Command '{command}' execution timeout (exceeded 5 minutes)");
            }

            return result.ToString();
        }
        catch (Exception ex) when (ex is not (TimeoutException or InvalidOperationException))
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

                    if (!_cdbProcess.WaitForExit(Constants.Debugging.ProcessWaitTimeout))
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