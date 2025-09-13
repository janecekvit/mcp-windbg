namespace Shared.Configuration;

/// <summary>
/// Configuration settings for debugger operations
/// </summary>
public class DebuggerConfiguration
{
    /// <summary>
    /// Path to the CDB executable. If null, auto-detection will be used.
    /// </summary>
    public string? CdbPath { get; set; }

    /// <summary>
    /// Path to the symbol cache directory. If null, default location will be used.
    /// </summary>
    public string? SymbolCache { get; set; }

    /// <summary>
    /// Additional symbol paths to include
    /// </summary>
    public string SymbolPathExtra { get; set; } = string.Empty;
}