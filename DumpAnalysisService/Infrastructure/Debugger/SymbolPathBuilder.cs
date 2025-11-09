using Shared.Configuration;

namespace BackgroundService.Infrastructure.Debugger;

/// <summary>
/// Infrastructure service for building WinDbg symbol paths.
/// Handles symbol server configuration, caching, and priority ordering.
/// </summary>
public sealed class SymbolPathBuilder
{
    private readonly string _symbolCache;
    private readonly string? _symbolPathExtra;
    private readonly string? _symbolServers;
    private readonly string[] _defaultSymbolServers;
    private readonly ILogger<SymbolPathBuilder> _logger;

    public SymbolPathBuilder(
        string symbolCache,
        string? symbolPathExtra,
        string? symbolServers,
        DebuggerConfiguration debuggerConfig,
        ILogger<SymbolPathBuilder> logger)
    {
        _symbolCache = symbolCache;
        _symbolPathExtra = symbolPathExtra;
        _symbolServers = symbolServers;
        _defaultSymbolServers = debuggerConfig.GetDefaultSymbolServers();
        _logger = logger;
    }

    /// <summary>
    /// Builds a comprehensive symbol path string for CDB
    /// Format: cache*<path>;srv*<url1>;srv*<url2>;...
    /// Priority: Cache > Extra paths > Custom servers > Default Microsoft servers
    /// </summary>
    public string BuildSymbolPath()
    {
        // Ensure symbol cache directory exists
        Directory.CreateDirectory(_symbolCache);

        var symbolPathParts = new List<string>
        {
            // FIRST: Add cache directive (WinDbg will use this for all srv* entries)
            $"cache*{_symbolCache}"
        };

        _logger.LogInformation("Symbol cache: {SymbolCache}", _symbolCache);

        // Add extra symbol paths (highest priority)
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
                    // It's a URL - add as srv*url (cache is already set)
                    symbolPathParts.Add($"srv*{trimmedServer}");
                }
                else
                {
                    // It's a file path - add directly
                    symbolPathParts.Add(trimmedServer);
                }
            }
            _logger.LogInformation("Using custom symbol servers: {SymbolServers}", _symbolServers);
        }

        // Add default symbol servers from configuration (lower priority)
        symbolPathParts.AddRange(_defaultSymbolServers);
        _logger.LogDebug("Added default symbol servers from configuration: {DefaultServers}", string.Join(", ", _defaultSymbolServers));

        var symbolPath = string.Join(";", symbolPathParts.Where(p => !string.IsNullOrWhiteSpace(p)));
        _logger.LogInformation("Built symbol path: {SymbolPath}", symbolPath);

        return symbolPath;
    }

    /// <summary>
    /// Gets the list of symbol initialization commands for CDB
    /// Note: Symbol path is already set via -y parameter, these commands just verify
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

            // Reload symbols - uses cache if available, downloads only if needed
            ".reload",

            // Wait for symbol loading to complete
            ".echo Waiting for symbol loading to complete...",

            // Verify symbol loading
            ".echo === Symbol Loading Status ===",
            "lm",
            ".echo === Session initialized successfully ===",
            ".echo"
        };

        return commands;
    }
}
