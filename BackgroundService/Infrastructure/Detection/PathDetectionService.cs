using BackgroundService.Infrastructure.IO;

namespace BackgroundService.Infrastructure.Detection;

/// <summary>
/// Infrastructure service for detecting CDB installations.
/// Searches Windows SDK paths, Windows Store apps, and validates debugger executables.
/// </summary>
public class PathDetectionService : IPathDetectionService
{
    private const string CdbExecutable = "cdb.exe";

    private static readonly IReadOnlyList<string> SearchPatterns = new List<string>
    {
        // Windows SDK (any version) - uses wildcards for Program Files variants, SDK version, and architectures
        @"C:\Program Files*\Windows Kits\*\Debuggers\*\cdb.exe",

        // WinDbg Store App - wildcard for version number and architecture
        @"C:\Program Files\WindowsApps\Microsoft.WinDbg*\*\cdb.exe"
    };

    private readonly ILogger<PathDetectionService> _logger;
    private readonly IPathExpansionService _pathExpansionService;

    public PathDetectionService(
        ILogger<PathDetectionService> logger,
        IPathExpansionService pathExpansionService)
    {
        _logger = logger;
        _pathExpansionService = pathExpansionService;
    }

    public (string? CdbPath, List<string> FoundPaths) DetectDebuggerPaths()
    {
        var foundPaths = new List<string>();
        string? cdbPath = null;

        // Expand wildcard patterns and try to find all available paths
        foreach (var pattern in SearchPatterns)
        {
            var expandedPaths = _pathExpansionService.ExpandWildcardPath(pattern);
            foreach (var path in expandedPaths)
            {
                if (File.Exists(path))
                {
                    foundPaths.Add(path);
                    _logger.LogInformation("Found debugger at: {Path}", path);

                    // Prefer CDB if not found yet
                    if (cdbPath == null && path.EndsWith(CdbExecutable, StringComparison.OrdinalIgnoreCase))
                    {
                        cdbPath = path;
                    }
                }
            }
        }

        return (cdbPath, foundPaths.Distinct().ToList());
    }


    public string GetBestDebuggerPath()
    {
        var (cdbPath, foundPaths) = DetectDebuggerPaths();

        if (!string.IsNullOrEmpty(cdbPath))
        {
            _logger.LogInformation("Selected debugger: {Path}", cdbPath);
            return cdbPath;
        }

        _logger.LogError("No debugger found. Install Windows SDK or WinDbg from Microsoft Store.");
        _logger.LogInformation("Searched patterns: {Patterns}", string.Join(", ", SearchPatterns));

        if (foundPaths.Any())
            _logger.LogInformation("Found alternative debuggers: {FoundPaths}", string.Join(", ", foundPaths));

        throw new FileNotFoundException(
            "CDB or WinDbg not found. Install Windows SDK or WinDbg from Microsoft Store.\n" +
            "Searched patterns:\n" + string.Join("\n", SearchPatterns) +
            (foundPaths.Any() ? "\n\nFound alternatives:\n" + string.Join("\n", foundPaths) : ""));
    }

    public bool ValidateDebuggerPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        if (!File.Exists(path))
        {
            _logger.LogError("Debugger not found at: {Path}", path);
            return false;
        }

        if (!path.EndsWith(CdbExecutable, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Path doesn't appear to be a valid debugger: {Path}", path);
            return false;
        }

        _logger.LogInformation("Validated debugger path: {Path}", path);
        return true;
    }
}
