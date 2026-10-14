namespace DumpAnalysisService.Security;

/// <summary>
/// Enforces Security:AllowedDumpDirectories for every operation that opens a dump file.
/// </summary>
public static class DumpPathPolicy
{
    /// <summary>
    /// Throws <see cref="UnauthorizedAccessException"/> when the dump lies outside the
    /// configured directories. An empty list allows any path the service can read.
    /// </summary>
    public static void EnsureAllowed(string dumpFilePath, SecurityOptions options)
    {
        var allowedDirectories = options.AllowedDumpDirectories;
        if (allowedDirectories.Count == 0)
            return;

        var fullPath = Path.GetFullPath(dumpFilePath);
        foreach (var directory in allowedDirectories.Where(d => !string.IsNullOrWhiteSpace(d)))
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return;
        }

        throw new UnauthorizedAccessException("Dump file is outside the directories allowed by Security:AllowedDumpDirectories");
    }
}
