namespace DumpAnalysisService.Infrastructure.IO;

/// <summary>
/// Service for expanding file system path patterns containing wildcards (* and ?).
/// Provides general-purpose path expansion functionality for any file system operation.
/// </summary>
public class PathExpansionService : IPathExpansionService
{
    private const string Wildcard = "*";
    private const string QuestionMark = "?";
    private const string DirectorySeparator = "\\";

    private readonly ILogger<PathExpansionService> _logger;

    public PathExpansionService(ILogger<PathExpansionService> logger)
    {
        _logger = logger;
    }

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
    public IEnumerable<string> ExpandWildcardPath(string pattern)
    {
        var paths = new List<string>();

        try
        {
            // Split the pattern into parts
            var parts = pattern.Split(new[] { DirectorySeparator }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0)
            {
                _logger.LogWarning("Empty path pattern provided");
                return paths;
            }

            // Start with the drive letter (e.g., "C:" -> "C:\")
            var driveLetter = parts[0];
            if (!driveLetter.EndsWith(DirectorySeparator))
            {
                driveLetter += DirectorySeparator;
            }
            var currentPaths = new List<string> { driveLetter };

            // Process each part of the path
            for (var i = 1; i < parts.Length; i++)
            {
                var part = parts[i];
                var nextPaths = new List<string>();

                foreach (var currentPath in currentPaths)
                {
                    try
                    {
                        if (part == Wildcard)
                        {
                            // Pure wildcard - get all subdirectories
                            if (Directory.Exists(currentPath))
                            {
                                nextPaths.AddRange(Directory.GetDirectories(currentPath));
                            }
                        }
                        else if (part.Contains(Wildcard) || part.Contains(QuestionMark))
                        {
                            // Pattern with wildcards - use as search pattern
                            if (Directory.Exists(currentPath))
                            {
                                // Determine if we're looking for files or directories
                                // If the pattern contains a file extension (e.g., "*.exe"), treat it as files
                                // Otherwise treat as directories
                                var isFilePattern = part.Contains('.') && i == parts.Length - 1;

                                if (isFilePattern)

                                    nextPaths.AddRange(Directory.GetFiles(currentPath, part, SearchOption.TopDirectoryOnly));
                                else
                                    nextPaths.AddRange(Directory.GetDirectories(currentPath, part, SearchOption.TopDirectoryOnly));
                            }
                        }
                        else
                        {
                            // No wildcard - just append
                            var nextPath = Path.Combine(currentPath, part);
                            if (Directory.Exists(nextPath) || File.Exists(nextPath))
                                nextPaths.Add(nextPath);
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Skip directories we can't access
                        _logger.LogTrace("Access denied to path: {Path}", currentPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogTrace(ex, "Error accessing path: {Path}", currentPath);
                    }
                }

                currentPaths = nextPaths;
            }

            paths.AddRange(currentPaths);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error expanding wildcard path: {Pattern}", pattern);
        }

        return paths;
    }
}
