namespace Shared.Configuration;

/// <summary>
/// Provides centralized application paths for CdbMcpServer
/// </summary>
public static class ApplicationPaths
{
    /// <summary>
    /// Gets the base application data directory.
    /// Returns: %LOCALAPPDATA%\CdbAnalysisServer
    /// </summary>
    public static string GetBaseDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CdbAnalysisServer");
    }

    /// <summary>
    /// Gets the logs directory for DumpAnalyser.
    /// Returns: %LOCALAPPDATA%\CdbAnalysisServer\logs
    /// </summary>
    public static string GetLogsDirectory()
    {
        return Path.Combine(GetBaseDirectory(), "logs");
    }

    /// <summary>
    /// Gets the default symbols cache directory.
    /// Returns: %LOCALAPPDATA%\CdbAnalysisServer\symbols
    /// </summary>
    public static string GetSymbolsDirectory()
    {
        return Path.Combine(GetBaseDirectory(), "symbols");
    }
}
