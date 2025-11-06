using System.Text.Json.Serialization;

namespace Shared.Configuration;

/// <summary>
/// Symbol configuration for debugger sessions (sent per-request from MCP server)
/// </summary>
public record SymbolsConfiguration(
    /// <summary>
    /// Symbol cache directory path
    /// Example: "D:\\Symbols"
    /// </summary>
    [property: JsonPropertyName("symbolCache")]
    string? SymbolCache = null,

    /// <summary>
    /// Additional local symbol paths (semicolon-separated)
    /// Example: "C:\\MySymbols;D:\\ProjectSymbols"
    /// </summary>
    [property: JsonPropertyName("symbolPathExtra")]
    string? SymbolPathExtra = null,

    /// <summary>
    /// Custom symbol servers (semicolon-separated URLs)
    /// Example: "https://artifacts.dev.azure.com/org/_apis/Symbol/symsrv"
    /// </summary>
    [property: JsonPropertyName("symbolServers")]
    string? SymbolServers = null);
