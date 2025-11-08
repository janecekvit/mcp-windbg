using System.Text;
using BackgroundService.Infrastructure.Debugger;
using Shared;
using Shared.Extensions;
using Shared.Models;

namespace BackgroundService.Services;

/// <summary>
/// Service for managing CDB debugging sessions.
/// Orchestrates debugger operations, command execution, and analysis workflows.
/// Uses infrastructure services for process management and symbol configuration.
/// </summary>
public sealed class CdbSessionService : ICdbSessionService
{
    private readonly ILogger<CdbSessionService> _logger;
    private readonly IAnalysisService _analysisService;
    private readonly ICdbProcessManager _processManager;
    private readonly SymbolPathBuilder _symbolPathBuilder;
    private readonly string _cdbPath;
    private bool _isInitialized;
    private readonly SemaphoreSlim _commandSemaphore = new(1, 1);

    public string SessionId { get; }
    public string? CurrentDumpFile { get; private set; }
    public bool IsActive => _processManager.IsActive;

    public CdbSessionService(
        string sessionId,
        ILogger<CdbSessionService> logger,
        IAnalysisService analysisService,
        ICdbProcessManager processManager,
        SymbolPathBuilder symbolPathBuilder,
        string cdbPath)
    {
        SessionId = sessionId;
        _logger = logger;
        _analysisService = analysisService;
        _processManager = processManager;
        _symbolPathBuilder = symbolPathBuilder;
        _cdbPath = cdbPath;
    }

    public async Task LoadDumpAsync(string dumpFilePath, IProgress<ProgressUpdate>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(dumpFilePath))
        {
            _logger.LogError("Dump file not found: {DumpFile}", dumpFilePath);
            throw new FileNotFoundException($"Dump file not found: {dumpFilePath}", dumpFilePath);
        }

        progress?.Report(ProgressUpdate.StartingCdb());
        CurrentDumpFile = dumpFilePath;

        // Build symbol path using infrastructure service
        var symbolPath = _symbolPathBuilder.BuildSymbolPath();

        if (!File.Exists(_cdbPath))
        {
            _logger.LogError("CDB not found at: {CdbPath}", _cdbPath);
            throw new FileNotFoundException($"CDB not found at: {_cdbPath}", _cdbPath);
        }

        // Start CDB process using infrastructure manager
        progress?.Report(ProgressUpdate.LoadingDump("Starting CDB process..."));
        var started = await _processManager.StartProcessAsync(_cdbPath, dumpFilePath, symbolPath);

        if (!started)
        {
            _logger.LogError("Failed to start CDB process for session {SessionId}", SessionId);
            throw new InvalidOperationException("Failed to start CDB process");
        }

        _logger.LogInformation("CDB process started for session {SessionId}, PID: {ProcessId}", SessionId, _processManager.ProcessId);

        // Initialize session with symbol configuration
        progress?.Report(ProgressUpdate.LoadingDump("CDB process started, initializing session..."));
        await _InitializeSessionAsync(progress, cancellationToken);

        _logger.LogInformation("CDB session {SessionId} loaded dump: {DumpFile}", SessionId, dumpFilePath);
    }

    public async Task CancelAsync()
    {
        _logger.LogWarning("Cancelling CDB session {SessionId}", SessionId);
        await _processManager.KillProcessAsync();
    }

    private async Task _InitializeSessionAsync(IProgress<ProgressUpdate>? progress, CancellationToken cancellationToken = default)
    {
        if (_isInitialized) return;

        _logger.LogInformation("Initializing CDB session {SessionId}", SessionId);
        progress?.Report(ProgressUpdate.ConfiguringSymbols());

        // Get initialization commands from infrastructure service
        var initCommands = _symbolPathBuilder.GetSymbolInitializationCommands();

        progress?.Report(ProgressUpdate.SettingSymbolPaths());
        var commandIndex = 0;

        foreach (var command in initCommands)
        {
            commandIndex++;

            // Report progress for key commands
            if (command.Contains(".reload"))
            {
                progress?.Report(ProgressUpdate.ResolvingSymbols("Loading symbols (this may take several minutes)..."));
            }
            else if (command == "lm")
            {
                progress?.Report(ProgressUpdate.VerifyingSymbols());
            }

            _logger.LogDebug("Executing init command for session {SessionId}: {Command}", SessionId, command);

            var result = await _ExecuteCommandInternalAsync(command, cancellationToken);

            // Check for symbol loading issues and retry if needed
            if (command == ".reload" && (result.Contains("WRONG_SYMBOLS") || result.Contains("MISSING")))
            {
                _logger.LogWarning("Symbol loading issues detected, attempting retry for session {SessionId}", SessionId);
                await _RetrySymbolLoadingAsync(cancellationToken);
            }

            // Log cache usage for symbol loading
            if (command == ".reload")
            {
                if (result.Contains("from cache") || result.Contains("cached"))
                {
                    _logger.LogInformation("Session {SessionId}: Symbols loaded from cache", SessionId);
                    progress?.Report(ProgressUpdate.LoadingFromCache());
                }
                else if (result.Contains("download") || result.Contains("SYMSRV"))
                {
                    _logger.LogInformation("Session {SessionId}: Symbols downloaded from symbol server", SessionId);
                    progress?.Report(ProgressUpdate.DownloadingSymbols());
                }
            }

            _logger.LogDebug("Init command result for session {SessionId}: {Result}", SessionId,
                result?.Length > Constants.Debugging.LogTruncateLength ? result[..Constants.Debugging.LogTruncateLength] + "..." : result);

            await Task.Delay(Constants.Debugging.InitializationDelay, cancellationToken);
        }

        _isInitialized = true;
        _logger.LogInformation("CDB session {SessionId} initialized successfully", SessionId);
    }

    private async Task _RetrySymbolLoadingAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Retrying symbol loading for session {SessionId}", SessionId);

        var retryCommands = new[]
        {
            ".symfix",
            ".reload /v",
            ".echo Retrying symbol loading...",
            ".reload",
            ".echo === Symbol Retry Status ===",
            "lm v",
        };

        foreach (var command in retryCommands)
        {
            try
            {
                _logger.LogDebug("Executing retry command for session {SessionId}: {Command}", SessionId, command);
                var result = await _ExecuteCommandInternalAsync(command, cancellationToken);
                _logger.LogDebug("Retry command result for session {SessionId}: {Result}", SessionId,
                    result?.Length > Constants.Debugging.LogTruncateLength ? result[..Constants.Debugging.LogTruncateLength] + "..." : result);
                await Task.Delay(Constants.Debugging.InitializationDelay * 2, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Retry command failed for session {SessionId}: {Command}", SessionId, command);
            }
        }
    }

    public async Task<string> ExecuteCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        return await ExecuteCommandAsync(command, null, cancellationToken);
    }

    public async Task<string> ExecuteCommandAsync(string command, IProgress<ProgressUpdate>? progress, CancellationToken cancellationToken = default)
    {
        if (!_processManager.IsActive)
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
            progress?.Report(ProgressUpdate.ExecutingCommand(command, 0.1));
            return await _ExecuteCommandInternalAsync(command, cancellationToken);
        }
        finally
        {
            _commandSemaphore.Release();
        }
    }

    private async Task<string> _ExecuteCommandInternalAsync(string command, CancellationToken cancellationToken = default)
    {
        if (!_processManager.IsActive)
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

            // Write command using infrastructure manager
            await _processManager.WriteCommandAsync(fullCommand);

            // Read output until marker using infrastructure manager
            var timeoutMs = (int)TimeSpan.FromMinutes(Constants.Debugging.SymbolLoadingTimeoutMinutes).TotalMilliseconds;
            var result = await _processManager.ReadUntilMarkerAsync(marker, timeoutMs, cancellationToken);

            return result;
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning("Command execution timeout for: {Command}", command);
            throw new TimeoutException($"Command '{command}' execution timeout", ex);
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
        return await ExecutePredefinedAnalysisAsync(analysisName, null, cancellationToken);
    }

    public async Task<string> ExecutePredefinedAnalysisAsync(string analysisName, IProgress<ProgressUpdate>? progress, CancellationToken cancellationToken = default)
    {
        progress?.Report(ProgressUpdate.Analyzing(analysisName, 0.1));

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

        var totalCommands = commands.Count;
        var currentCommand = 0;

        foreach (var command in commands)
        {
            currentCommand++;
            var progressPercent = 0.1 + (0.8 * currentCommand / totalCommands);
            progress?.Report(ProgressUpdate.Analyzing($"{analysisName} ({currentCommand}/{totalCommands})", progressPercent));

            var result = await ExecuteCommandAsync(command, cancellationToken);
            results.AppendLine(result);
            results.AppendLine();
        }

        return results.ToString();
    }

    public void Dispose()
    {
        try
        {
            if (_processManager.IsActive)
            {
                _processManager.ShutdownGracefullyAsync(Constants.Debugging.ProcessWaitTimeout).GetAwaiter().GetResult();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing CDB session {SessionId}", SessionId);
        }
        finally
        {
            _processManager.Dispose();
            _commandSemaphore.Dispose();
        }
    }
}
