namespace BackgroundService.Infrastructure.Debugger;

/// <summary>
/// Infrastructure service for building WinDbg symbol paths.
/// Handles symbol server configuration, caching, and priority ordering.
/// </summary>
public sealed class SymbolPathBuilder
{
    private readonly string _symbolCache;
    private readonly string _symbolPathExtra;
    private readonly string? _symbolServers;
    private readonly ILogger<SymbolPathBuilder> _logger;

    public SymbolPathBuilder(
        string symbolCache,
        string symbolPathExtra,
        string? symbolServers,
        ILogger<SymbolPathBuilder> logger)
    {
        _symbolCache = symbolCache;
        _symbolPathExtra = symbolPathExtra;
        _symbolServers = symbolServers;
        _logger = logger;
    }

    /// <summary>
    /// Builds a comprehensive symbol path string for CDB
    /// Priority: Extra paths > Custom servers > Default Microsoft servers
    /// </summary>
    public string BuildSymbolPath()
    {
        // Ensure symbol cache directory exists
        Directory.CreateDirectory(_symbolCache);

        var symbolPathParts = new List<string>();

        // Add extra symbol paths first (highest priority)
        if (!string.IsNullOrWhiteSpace(_symbolPathExtra))
        {
            symbolPathParts.AddRange(_symbolPathExtra.Split(';', StringSplitOptions.RemoveEmptyEntries));
            _logger.LogDebug("Added extra symbol paths: {ExtraPaths}", _symbolPathExtra);
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
                    symbolPathParts.Add($"srv*{_symbolCache}*{trimmedServer}");
                }
                else
                {
                    // It's a file path - add directly
                    symbolPathParts.Add(trimmedServer);
                }
            }
            _logger.LogInformation("Using custom symbol servers: {SymbolServers}", _symbolServers);
        }

        // Add default Microsoft symbol servers (lower priority)
        var defaultServers = new[]
        {
            $"srv*{_symbolCache}*https://msdl.microsoft.com/download/symbols",
            $"srv*{_symbolCache}*https://symbols.nuget.org/download/symbols",
            $"srv*{_symbolCache}*https://download.microsoft.com/download/symbols"
        };
        symbolPathParts.AddRange(defaultServers);

        var symbolPath = string.Join(";", symbolPathParts.Where(p => !string.IsNullOrWhiteSpace(p)));
        _logger.LogDebug("Built symbol path: {SymbolPath}", symbolPath);

        return symbolPath;
    }

    /// <summary>
    /// Gets the list of symbol initialization commands for CDB
    /// </summary>
    public List<string> GetSymbolInitializationCommands()
    {
        var commands = new List<string>
        {
            // Set symbol options for better debugging
            ".symopt+ 0x40",          // SYMOPT_DEFERRED_LOADS
            ".symopt+ 0x400",         // SYMOPT_NO_PROMPTS
            ".symopt+ 0x800",         // SYMOPT_FAIL_CRITICAL_ERRORS
            ".symopt- 0x2",           // SYMOPT_UNDNAME (disable for cleaner output)
        };

        // Add custom symbol servers to init commands
        if (!string.IsNullOrWhiteSpace(_symbolServers))
        {
            foreach (var server in _symbolServers.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmedServer = server.Trim();
                if (trimmedServer.StartsWith("http://") || trimmedServer.StartsWith("https://"))
                {
                    commands.Add($".sympath+ srv*{_symbolCache}*{trimmedServer}");
                }
                else
                {
                    commands.Add($".sympath+ {trimmedServer}");
                }
            }
        }

        // Add default Microsoft symbol servers
        commands.AddRange(new[]
        {
            $".sympath+ srv*{_symbolCache}*https://msdl.microsoft.com/download/symbols",
            $".sympath+ srv*{_symbolCache}*https://symbols.nuget.org/download/symbols",

            // Reload symbols - uses cache if available, downloads only if needed
            ".reload",

            // Wait for symbol loading to complete
            ".echo Waiting for symbol loading to complete...",

            // Verify symbol loading
            ".echo === Symbol Loading Status ===",
            "lm",
            ".echo === Session initialized successfully ===",
            ".echo"
        });

        return commands;
    }
}
