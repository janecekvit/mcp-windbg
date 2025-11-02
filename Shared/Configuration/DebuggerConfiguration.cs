namespace Shared.Configuration;

/// <summary>
/// Configuration settings for debugger operations
/// </summary>
public class DebuggerConfiguration
{
    /// <summary>
    /// Path to the symbol cache directory.
    /// Default: %LOCALAPPDATA%\CdbMcpServer\symbols
    /// </summary>
    public string SymbolCache { get; set; } = string.Empty;

    /// <summary>
    /// Additional symbol paths to include
    /// </summary>
    public string SymbolPathExtra { get; set; } = string.Empty;

    /// <summary>
    /// Custom symbol servers (semicolon-separated URLs or file paths)
    /// Example: "https://your-symbol-server.com/symbols;C:\MySymbols"
    /// </summary>
    public string? SymbolServers { get; set; }
}