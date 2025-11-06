using System.Runtime.InteropServices;
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

    public List<string> DetectDebuggerPaths()
    {
        var foundPaths = new List<string>();

        // Expand wildcard patterns and try to find all available paths
        foreach (var pattern in SearchPatterns)
        {
            var expandedPaths = _pathExpansionService.ExpandWildcardPath(pattern);
            foreach (var path in expandedPaths)
            {
                if (File.Exists(path) && path.EndsWith(CdbExecutable, StringComparison.OrdinalIgnoreCase))
                {
                    foundPaths.Add(path);
                    _logger.LogInformation("Found debugger at: {Path}", path);
                }
            }
        }

        var distinctPaths = foundPaths.Distinct().ToList();
        _logger.LogInformation("Found paths: {Paths}", string.Join(", ", distinctPaths));
        return distinctPaths;
    }

    public string? GetBestDebuggerPath()
    {
        var paths = DetectDebuggerPaths();

        if (!paths.Any())
        {
            _logger.LogError("No debugger found. Install Windows SDK or WinDbg from Microsoft Store.");
            _logger.LogInformation("Searched patterns: {Patterns}", string.Join(", ", SearchPatterns));

            throw new FileNotFoundException(
                "CDB not found. Install Windows SDK or WinDbg from Microsoft Store.\n" +
                "Searched patterns:\n" + string.Join("\n", SearchPatterns));
        }

        // Detect system architecture
        var systemArch = RuntimeInformation.OSArchitecture;
        _logger.LogInformation("Detected system architecture: {Architecture}", systemArch);

        // Score each path based on priority criteria (including system architecture)
        var scoredPaths = paths.Select(path => new
        {
            Path = path,
            Score = CalculatePathScore(path, systemArch)
        }).ToList();

        // Sort by score (highest first) and select the best
        var bestPath = scoredPaths.OrderByDescending(x => x.Score).First();

        _logger.LogInformation("Selected debugger: {Path} (score: {Score}, architecture-aware)",
            bestPath.Path, bestPath.Score);

        // Log other candidates for debugging
        if (scoredPaths.Count > 1)
        {
            var otherPaths = scoredPaths.Where(x => x.Path != bestPath.Path).Take(3);
            foreach (var candidate in otherPaths)
            {
                _logger.LogDebug("Alternative debugger: {Path} (score: {Score})", candidate.Path, candidate.Score);
            }
        }

        return bestPath.Path;
    }

    /// <summary>
    /// Calculates a priority score for a debugger path based on system architecture.
    /// Higher score = better choice.
    ///
    /// Priority:
    /// 1. Source: WindowsApps (Store) > Windows SDK
    /// 2. Architecture match: Native architecture gets highest score
    ///    - On X64: amd64/x64 preferred
    ///    - On ARM64: arm64 preferred
    ///    - On X86: x86 required (64-bit won't run)
    /// </summary>
    private static int CalculatePathScore(string path, Architecture systemArchitecture)
    {
        var score = 0;

        // Source priority: WindowsApps (Store app) is preferred (newer, auto-updated)
        if (path.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
        }
        else if (path.Contains("Windows Kits", StringComparison.OrdinalIgnoreCase))
        {
            score += 50;
        }

        // Architecture priority: Match system architecture for optimal performance
        score += GetArchitectureScore(path, systemArchitecture);

        return score;
    }

    /// <summary>
    /// Calculates architecture-specific score based on system architecture.
    /// Simple matching: X64 → amd64/x64, ARM64 → arm64, X86 → x86
    /// </summary>
    private static int GetArchitectureScore(string path, Architecture systemArchitecture)
    {
        var isAmd64 = path.Contains("\\amd64\\", StringComparison.OrdinalIgnoreCase);
        var isX64 = path.Contains("\\x64\\", StringComparison.OrdinalIgnoreCase);
        var isArm64 = path.Contains("\\arm64\\", StringComparison.OrdinalIgnoreCase);
        var isX86 = path.Contains("\\x86\\", StringComparison.OrdinalIgnoreCase);

        return systemArchitecture switch
        {
            // X64 systems → use amd64 or x64
            Architecture.X64 when isAmd64 || isX64 => 50,

            // ARM64 systems → use arm64
            Architecture.Arm64 when isArm64 => 50,

            // X86 systems → use x86
            Architecture.X86 when isX86 => 50,

            // No match
            _ => 0
        };
    }
}
