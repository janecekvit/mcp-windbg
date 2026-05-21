using Microsoft.Extensions.Logging;
using Shared;
using Shared.Client;
using Shared.Configuration;

namespace CdbDebuggerClient;

internal class Program
{
    private const string _baseUrl = Constants.Network.DefaultServiceUrl;

    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            _PrintUsage();
            return 1;
        }

        try
        {
            // Parse command line arguments
            var (command, commandArgs, symbols) = _ParseArguments(args);

            if (command == null)
            {
                _PrintUsage();
                return 1;
            }

            // Setup logging
            using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

            // Setup HTTP client
            var httpClient = new HttpClient();

            // Setup SignalR client
            var signalRLogger = loggerFactory.CreateLogger<SignalRClientService>();
            var signalRClient = new SignalRClientService(signalRLogger, Constants.Network.DefaultProgressHubUrl);

            // Connect to SignalR hub
            await signalRClient.ConnectAsync();
            Console.WriteLine($"Connected to Dump Analysis Service at {_baseUrl}");

            // Setup debugger API service
            var apiLogger = loggerFactory.CreateLogger<DebuggerApiService>();
            var apiService = new DebuggerApiService(apiLogger, httpClient, signalRClient, _baseUrl, symbols);

            var result = command switch
            {
                "load" => await _LoadDumpAsync(apiService, commandArgs),
                "exec" => await _ExecuteCommandAsync(apiService, commandArgs),
                "analyze" => await _BasicAnalysisAsync(apiService, commandArgs),
                "close" => await _CloseSessionAsync(apiService, commandArgs),
                "list-jobs" => await _ListJobsAsync(apiService, commandArgs),
                "detect" => await _DetectDebuggersAsync(apiService),
                "help" => _PrintUsage(),
                _ => _PrintUsage()
            };

            await signalRClient.DisposeAsync();
            return result;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Parses command line arguments and extracts options and command
    /// </summary>
    /// <returns>Tuple of (command, commandArgs, symbolsConfiguration)</returns>
    private static (string? command, string[] commandArgs, SymbolsConfiguration symbols) _ParseArguments(string[] args)
    {
        string? symbolCache = null;
        string? symbolPathExtra = null;
        string? symbolServers = null;
        var commandArgsList = new List<string>();
        string? command = null;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg.StartsWith("--"))
            {
                // Parse option
                switch (arg.ToLowerInvariant())
                {
                    case "--symbol-cache":
                        if (i + 1 < args.Length)
                        {
                            symbolCache = args[++i];
                        }
                        else
                        {
                            Console.Error.WriteLine("Error: --symbol-cache requires a value");
                            return (null, Array.Empty<string>(), new SymbolsConfiguration(null, null, null));
                        }
                        break;

                    case "--symbol-path-extra":
                        if (i + 1 < args.Length)
                        {
                            symbolPathExtra = args[++i];
                        }
                        else
                        {
                            Console.Error.WriteLine("Error: --symbol-path-extra requires a value");
                            return (null, Array.Empty<string>(), new SymbolsConfiguration(null, null, null));
                        }
                        break;

                    case "--symbol-servers":
                        if (i + 1 < args.Length)
                        {
                            symbolServers = args[++i];
                        }
                        else
                        {
                            Console.Error.WriteLine("Error: --symbol-servers requires a value");
                            return (null, Array.Empty<string>(), new SymbolsConfiguration(null, null, null));
                        }
                        break;

                    default:
                        Console.Error.WriteLine($"Error: Unknown option '{arg}'");
                        return (null, Array.Empty<string>(), new SymbolsConfiguration(null, null, null));
                }
            }
            else
            {
                // First non-option argument is the command
                if (command == null)
                {
                    command = arg.ToLowerInvariant();
                }
                else
                {
                    commandArgsList.Add(arg);
                }
            }
        }

        // Fallback to environment variables if not provided via command line
        symbolCache ??= Environment.GetEnvironmentVariable("SYMBOL_CACHE");
        symbolPathExtra ??= Environment.GetEnvironmentVariable("SYMBOL_PATH_EXTRA");
        symbolServers ??= Environment.GetEnvironmentVariable("SYMBOL_SERVERS");

        var symbols = new SymbolsConfiguration(
            SymbolCache: symbolCache,
            SymbolPathExtra: symbolPathExtra,
            SymbolServers: symbolServers);

        // Prepend command to commandArgs for backward compatibility with existing methods
        var finalCommandArgs = command != null
            ? new[] { command }.Concat(commandArgsList).ToArray()
            : Array.Empty<string>();

        return (command, finalCommandArgs, symbols);
    }

    private static async Task<int> _LoadDumpAsync(IDebuggerApiService apiService, string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: CdbDebuggerClient load <dump-file-path>");
            return 1;
        }

        var dumpFilePath = args[1];
        if (!File.Exists(dumpFilePath))
        {
            Console.Error.WriteLine($"Dump file not found: {dumpFilePath}");
            return 1;
        }

        Console.WriteLine($"Loading dump: {dumpFilePath}");

        // Create progress reporter
        var progress = new Progress<ModelContextProtocol.ProgressNotificationValue>(notification =>
        {
            var percent = notification.Total > 0 ? (notification.Progress / notification.Total * 100) : notification.Progress;
            Console.Write($"\r{notification.Message ?? "Processing"}: {percent:F1}%".PadRight(80));
        });

        var result = await apiService.LoadDumpAsync(dumpFilePath, progress);
        Console.WriteLine($"\n\nSession created: {result}");
        return 0;
    }

    private static async Task<int> _ExecuteCommandAsync(IDebuggerApiService apiService, string[] args)
    {
        if (args.Length < 3)
        {
            Console.WriteLine("Usage: CdbDebuggerClient exec <session-id> <command>");
            return 1;
        }

        var sessionId = args[1];
        var command = string.Join(" ", args.Skip(2));

        Console.WriteLine($"Executing command in session {sessionId}: {command}");

        var progress = new Progress<ModelContextProtocol.ProgressNotificationValue>(notification =>
        {
            var percent = notification.Total > 0 ? (notification.Progress / notification.Total * 100) : notification.Progress;
            Console.Write($"\r{notification.Message ?? "Processing"}: {percent:F1}%".PadRight(80));
        });

        var result = await apiService.ExecuteCommandAsync(sessionId, command, progress);
        Console.WriteLine($"\n\nResult:\n{result}");
        return 0;
    }

    private static async Task<int> _BasicAnalysisAsync(IDebuggerApiService apiService, string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: CdbDebuggerClient analyze <session-id>");
            return 1;
        }

        var sessionId = args[1];
        Console.WriteLine($"Running basic analysis on session {sessionId}");

        var progress = new Progress<ModelContextProtocol.ProgressNotificationValue>(notification =>
        {
            var percent = notification.Total > 0 ? (notification.Progress / notification.Total * 100) : notification.Progress;
            Console.Write($"\r{notification.Message ?? "Processing"}: {percent:F1}%".PadRight(80));
        });

        var result = await apiService.BasicAnalysisAsync(sessionId, progress);
        Console.WriteLine($"\n\nAnalysis Result:\n{result}");
        return 0;
    }

    private static async Task<int> _CloseSessionAsync(IDebuggerApiService apiService, string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: CdbDebuggerClient close <session-id>");
            return 1;
        }

        var sessionId = args[1];
        Console.WriteLine($"Closing session {sessionId}");

        var progress = new Progress<ModelContextProtocol.ProgressNotificationValue>(notification =>
        {
            var percent = notification.Total > 0 ? (notification.Progress / notification.Total * 100) : notification.Progress;
            Console.Write($"\r{notification.Message ?? "Processing"}: {percent:F1}%".PadRight(80));
        });

        var result = await apiService.CloseSessionAsync(sessionId, progress);
        Console.WriteLine($"\n{result}");
        return 0;
    }

    private static async Task<int> _ListJobsAsync(IDebuggerApiService apiService, string[] args)
    {
        var state = args.Length > 1 ? args[1] : null;
        var result = await apiService.ListJobsAsync(state);
        Console.WriteLine(result);
        return 0;
    }

    private static async Task<int> _DetectDebuggersAsync(IDebuggerApiService apiService)
    {
        var result = await apiService.DetectDebuggersAsync();
        Console.WriteLine(result);
        return 0;
    }

    private static int _PrintUsage()
    {
        Console.WriteLine("CdbDebuggerClient - Command line client for Dump Analysis Service HTTP API");
        Console.WriteLine("Uses SignalR for real-time progress monitoring");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  CdbDebuggerClient [options] <command> [command-args]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --symbol-cache <path>       Symbol cache directory");
        Console.WriteLine("                              (falls back to SYMBOL_CACHE env var)");
        Console.WriteLine("  --symbol-path-extra <path>  Additional symbol paths");
        Console.WriteLine("                              (falls back to SYMBOL_PATH_EXTRA env var)");
        Console.WriteLine("  --symbol-servers <servers>  Symbol servers (semicolon-separated)");
        Console.WriteLine("                              (falls back to SYMBOL_SERVERS env var)");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  load <dump-file-path>       Load a memory dump");
        Console.WriteLine("  exec <session-id> <command> Execute CDB command");
        Console.WriteLine("  analyze <session-id>        Run basic analysis");
        Console.WriteLine("  close <session-id>          Close session");
        Console.WriteLine("  list-jobs [state]           List all jobs");
        Console.WriteLine("  detect                      Detect debuggers");
        Console.WriteLine("  help                        Show this help");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  CdbDebuggerClient load \"C:\\dumps\\crash.dmp\"");
        Console.WriteLine("  CdbDebuggerClient --symbol-cache \"C:\\symbols\" load \"C:\\dumps\\crash.dmp\"");
        Console.WriteLine("  CdbDebuggerClient exec abc12345 \"!analyze -v\"");
        Console.WriteLine("  CdbDebuggerClient analyze abc12345");
        Console.WriteLine("  CdbDebuggerClient list-jobs Running");
        Console.WriteLine();
        Console.WriteLine("Features:");
        Console.WriteLine("  - Real-time progress via SignalR WebSocket");
        Console.WriteLine("  - Automatic polling fallback if SignalR fails");
        Console.WriteLine("  - Shared services with MCP server implementation");
        Console.WriteLine("  - Symbol configuration via command line or environment variables");
        return 0;
    }
}
