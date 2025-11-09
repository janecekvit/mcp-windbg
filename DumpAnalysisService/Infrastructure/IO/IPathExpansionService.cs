namespace DumpAnalysisService.Infrastructure.IO;

/// <summary>
/// Service for expanding file system path patterns containing wildcards.
/// Supports * (matches any characters) and ? (matches single character).
/// </summary>
public interface IPathExpansionService
{
    /// <summary>
    /// Expands a file path pattern containing wildcards into concrete file or directory paths.
    /// </summary>
    /// <param name="pattern">
    /// Path pattern with wildcards. Examples:
    /// - "C:\Program Files*\Windows Kits\*\cdb.exe" (directories and files)
    /// - "C:\Windows\System32\*.exe" (files only)
    /// - "C:\*\SomeFolder" (directories only)
    /// </param>
    /// <returns>Collection of matching file or directory paths</returns>
    /// <remarks>
    /// Patterns containing a dot (.) in the last segment are treated as file patterns.
    /// All other patterns are treated as directory patterns.
    /// </remarks>
    IEnumerable<string> ExpandWildcardPath(string pattern);
}
