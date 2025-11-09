namespace Shared.Configuration;

/// <summary>
/// Provides centralized application paths for CdbMcpServer
/// </summary>
public static class ApplicationPaths
{
    /// <summary>
    /// Gets the base application data directory.
    /// Returns: %LOCALAPPDATA%\DumpAnalysisService
    /// </summary>
    public static string GetBaseDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DumpAnalysisService");
    }

    /// <summary>
    /// Gets the logs directory for DumpAnalyser.
    /// Returns: %LOCALAPPDATA%\DumpAnalysisService\logs
    /// </summary>
    public static string GetLogsDirectory()
    {
        return Path.Combine(GetBaseDirectory(), "logs");
    }

    /// <summary>
    /// Gets the default symbols cache directory.
    /// Returns: %LOCALAPPDATA%\DumpAnalysisService\symbols
    /// </summary>
    public static string GetSymbolsDirectory()
    {
        return Path.Combine(GetBaseDirectory(), "symbols");
    }
}
