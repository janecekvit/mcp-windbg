using System.Diagnostics;
using System.Text;

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

    public async Task<bool> LoadDumpAsync(string dumpFilePath)
    {
        if (!File.Exists(dumpFilePath))
        {
            _logger.LogError("Dump file not found: {DumpFile}", dumpFilePath);
            return false;
        }

        if (!File.Exists(_cdbPath))
        {
            _logger.LogError("CDB not found at: {CdbPath}", _cdbPath);
            return false;
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

            _cdbProcess = Process.Start(startInfo);

            if (_cdbProcess == null)
            {
                _logger.LogError("Failed to start CDB process");
                return false;
            }

            _stdin = _cdbProcess.StandardInput;
            _isInitialized = false;
        }

        // Wait for initialization and set symbols
        await InitializeSessionAsync();

        _logger.LogInformation("CDB session {SessionId} loaded dump: {DumpFile}", SessionId, dumpFilePath);
        return true;
    }

    private async Task InitializeSessionAsync()
    {
        if (_isInitialized) return;

        var initCommands = new[]
        {
            $".symfix {_symbolCache}",
            ".symopt+ 0x40",
            ".reload",
            ".echo Session initialized successfully"
        };

        foreach (var command in initCommands)
        {
            await ExecuteCommandInternalAsync(command);
            await Task.Delay(500); // Short pause between commands
        }

        _isInitialized = true;
    }

    public async Task<string> ExecuteCommandAsync(string command)
    {
        if (_cdbProcess?.HasExited != false)
            return "Error: CDB process is not running. Load a dump file first.";

        if (!_isInitialized)
            return "Error: Session not initialized. Load a dump file first.";

        return await ExecuteCommandInternalAsync(command);
    }

    private async Task<string> ExecuteCommandInternalAsync(string command)
    {
        if (_stdin == null || _cdbProcess?.HasExited != false)
            return "Error: CDB process is not available";

        try
        {
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            // Use unique marker to identify end of output
            var marker = $"__END_COMMAND_{Guid.NewGuid():N}__";
            var fullCommand = $"{command}; .echo {marker}";

            lock (_lock)
            {
                _stdin.WriteLine(fullCommand);
                _stdin.Flush();
            }

            // Read output until we find the marker
            var outputTask = Task.Run(async () =>
            {
                var reader = _cdbProcess.StandardOutput;
                var buffer = new char[1024];
                var result = new StringBuilder();

                while (!_cdbProcess.HasExited)
                {
                    var bytesRead = await reader.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;

                    var chunk = new string(buffer, 0, bytesRead);
                    result.Append(chunk);

                    // Check for marker
                    if (result.ToString().Contains(marker))
                    {
                        var output = result.ToString();
                        var markerIndex = output.LastIndexOf(marker);
                        return output[..markerIndex].Trim();
                    }
                }

                return result.ToString();
            });

            // Command timeout
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30));
            var completedTask = await Task.WhenAny(outputTask, timeoutTask);

            if (completedTask == timeoutTask)
                return "Error: Command execution timeout";

            return await outputTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing command: {Command}", command);
            return $"Error executing command: {ex.Message}";
        }
    }

    public async Task<string> ExecuteBasicAnalysisAsync()
    {
        return await ExecutePredefinedAnalysisAsync("basic");
    }

    public async Task<string> ExecutePredefinedAnalysisAsync(string analysisName)
    {
        var commands = _analysisService.GetAnalysisCommands(analysisName);
        if (commands.Length == 0)
            return $"Unknown analysis type: {analysisName}. Available analyses: {string.Join(", ", _analysisService.GetAvailableAnalyses())}";

        var results = new StringBuilder();
        results.AppendLine($"Executing {analysisName} analysis:");
        results.AppendLine($"Description: {_analysisService.GetAnalysisDescription(analysisName)}");
        results.AppendLine();

        foreach (var command in commands)
        {
            var result = await ExecuteCommandAsync(command);
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
            }
        }
        GC.SuppressFinalize(this);
    }
}