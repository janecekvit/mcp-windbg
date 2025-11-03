namespace Shared.Configuration;

/// <summary>
/// Configuration for CDB debugger and symbol handling in BackgroundService.
/// These are server-side defaults that can be overridden by per-request SymbolsConfiguration.
/// </summary>
public class DebuggerConfiguration
{
    /// <summary>
    /// Default symbol cache directory path.
    /// If not specified, uses: %LOCALAPPDATA%\CdbAnalysisServer\Symbols
    /// </summary>
    public string? DefaultSymbolCache { get; set; }

    /// <summary>
    /// Gets or sets an optional additional path to be appended to the default symbol path.
    /// </summary>
    /// <remarks>This property allows customization of the default symbol path by appending an extra path
    /// segment.  It can be useful for including additional directories where symbols are stored.</remarks>
    public string? DefaultSymbolPathExtra { get; set; }

    /// <summary>
    /// Default Microsoft symbol servers (semicolon-separated).
    /// If not specified, uses standard Microsoft symbol servers.
    /// </summary>
    public string? DefaultSymbolServers { get; set; }

    /// <summary>
    /// Gets the effective symbol cache path, using environment fallback if needed.
    /// </summary>
    public string GetSymbolCachePath()
    {
        if (!string.IsNullOrWhiteSpace(DefaultSymbolCache))
            return DefaultSymbolCache;

        // Fallback to default location
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CdbAnalysisServer",
            "Symbols");
    }

    /// <summary>
    /// Gets the list of default symbol servers, split by semicolon.
    /// Returns hardcoded Microsoft servers if not configured.
    /// </summary>
    public string[] GetDefaultSymbolServers()
    {
        if (!string.IsNullOrWhiteSpace(DefaultSymbolServers))
        {
            return DefaultSymbolServers
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .ToArray();
        }

        // Fallback to Microsoft default servers
        return new[]
        {
            "srv*https://msdl.microsoft.com/download/symbols",
            "srv*https://symbols.nuget.org/download/symbols",
            "srv*https://download.microsoft.com/download/symbols"
        };
    }
}
