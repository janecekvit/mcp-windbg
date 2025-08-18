using Microsoft.Extensions.Logging;

namespace CdbBackgroundService;

public static class CdbPathDetector
{
    private static readonly string[] PotentialPaths = new[]
    {
        // Windows SDK (classic install)
        @"C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\cdb.exe",
        @"C:\Program Files (x86)\Windows Kits\10\Debuggers\x86\cdb.exe",
        @"C:\Program Files\Windows Kits\10\Debuggers\x64\cdb.exe",
        @"C:\Program Files\Windows Kits\10\Debuggers\x86\cdb.exe",
        
        // Windows 11 SDK (newer versions)
        @"C:\Program Files (x86)\Windows Kits\11\Debuggers\x64\cdb.exe",
        @"C:\Program Files (x86)\Windows Kits\11\Debuggers\x86\cdb.exe",
        @"C:\Program Files\Windows Kits\11\Debuggers\x64\cdb.exe",
        @"C:\Program Files\Windows Kits\11\Debuggers\x86\cdb.exe",
        
        // WinDbg Store App - use windbg.exe as fallback
        @"C:\Program Files\WindowsApps\Microsoft.WinDbg_1.2506.12002.0_x64__8wekyb3d8bbwe\amd64\windbg.exe",
        @"C:\Program Files\WindowsApps\Microsoft.WinDbg_1.2506.12002.0_x64__8wekyb3d8bbwe\x86\windbg.exe",
    };

    public static (string? CdbPath, string? WinDbgPath, List<string> FoundPaths) DetectDebuggerPaths(ILogger? logger = null)
    {
        var foundPaths = new List<string>();
        string? cdbPath = null;
        string? winDbgPath = null;

        // Try to find all available paths
        foreach (var path in PotentialPaths)
        {
            if (File.Exists(path))
            {
                foundPaths.Add(path);
                logger?.LogInformation("Found debugger at: {Path}", path);

                // Prefer CDB if not found yet
                if (cdbPath == null && path.EndsWith("cdb.exe", StringComparison.OrdinalIgnoreCase))
                {
                    cdbPath = path;
                }
                // Zapamatuj si WinDbg jako fallback
                else if (winDbgPath == null && path.EndsWith("windbg.exe", StringComparison.OrdinalIgnoreCase))
                {
                    winDbgPath = path;
                }
            }
        }

        // Also try to find WinDbg in WindowsApps using wildcard search
        var winDbgFromStore = FindWinDbgFromStore(logger);
        if (!string.IsNullOrEmpty(winDbgFromStore))
        {
            foundPaths.Add(winDbgFromStore);
            if (winDbgPath == null)
            {
                winDbgPath = winDbgFromStore;
            }
        }

        // Try to find Windows SDK via registry or common locations
        var sdkPaths = FindWindowsSdkPaths(logger);
        foundPaths.AddRange(sdkPaths);

        // If no CDB but WinDbg available, use WinDbg
        if (cdbPath == null && winDbgPath != null)
        {
            logger?.LogWarning("CDB not found, using WinDbg as fallback: {WinDbgPath}", winDbgPath);
            cdbPath = winDbgPath;
        }

        return (cdbPath, winDbgPath, foundPaths.Distinct().ToList());
    }

    private static string? FindWinDbgFromStore(ILogger? logger)
    {
        try
        {
            var windowsAppsPath = @"C:\Program Files\WindowsApps";
            if (!Directory.Exists(windowsAppsPath))
                return null;

            // Look for Microsoft.WinDbg* folders
            var winDbgDirs = Directory.GetDirectories(windowsAppsPath, "Microsoft.WinDbg*", SearchOption.TopDirectoryOnly);
            
            foreach (var dir in winDbgDirs)
            {
                // Zkus amd64 verzi
                var amd64Path = Path.Combine(dir, "amd64", "windbg.exe");
                if (File.Exists(amd64Path))
                {
                    logger?.LogInformation("Found WinDbg Store app (amd64): {Path}", amd64Path);
                    return amd64Path;
                }

                // Zkus x86 verzi
                var x86Path = Path.Combine(dir, "x86", "windbg.exe");
                if (File.Exists(x86Path))
                {
                    logger?.LogInformation("Found WinDbg Store app (x86): {Path}", x86Path);
                    return x86Path;
                }

                // Try cdb.exe in store app (some versions have it)
                var cdbAmd64Path = Path.Combine(dir, "amd64", "cdb.exe");
                if (File.Exists(cdbAmd64Path))
                {
                    logger?.LogInformation("Found CDB in Store app (amd64): {Path}", cdbAmd64Path);
                    return cdbAmd64Path;
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Error searching for WinDbg in WindowsApps");
        }

        return null;
    }

    private static List<string> FindWindowsSdkPaths(ILogger? logger)
    {
        var paths = new List<string>();
        
        try
        {
            // Try to find Windows SDK via standard paths
            var potentialSdkRoots = new[]
            {
                @"C:\Program Files (x86)\Windows Kits",
                @"C:\Program Files\Windows Kits"
            };

            foreach (var sdkRoot in potentialSdkRoots)
            {
                if (!Directory.Exists(sdkRoot))
                    continue;

                // Look for various SDK versions (10, 11)
                var versionDirs = Directory.GetDirectories(sdkRoot).Where(d => 
                    Path.GetFileName(d) is "10" or "11");

                foreach (var versionDir in versionDirs)
                {
                    var debuggerDir = Path.Combine(versionDir, "Debuggers");
                    if (!Directory.Exists(debuggerDir))
                        continue;

                    // Zkus x64 a x86 verze
                    foreach (var arch in new[] { "x64", "x86" })
                    {
                        var cdbPath = Path.Combine(debuggerDir, arch, "cdb.exe");
                        if (File.Exists(cdbPath))
                        {
                            paths.Add(cdbPath);
                            logger?.LogInformation("Found Windows SDK CDB ({Arch}): {Path}", arch, cdbPath);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Error searching for Windows SDK debuggers");
        }

        return paths;
    }

    public static string GetBestDebuggerPath(ILogger? logger = null)
    {
        var (cdbPath, winDbgPath, foundPaths) = DetectDebuggerPaths(logger);

        if (!string.IsNullOrEmpty(cdbPath))
        {
            logger?.LogInformation("Selected debugger: {Path}", cdbPath);
            return cdbPath;
        }

        logger?.LogError("No debugger found. Install Windows SDK or WinDbg from Microsoft Store.");
        logger?.LogInformation("Searched paths: {Paths}", string.Join(", ", PotentialPaths));
        
        if (foundPaths.Any())
        {
            logger?.LogInformation("Found alternative debuggers: {FoundPaths}", string.Join(", ", foundPaths));
        }

        throw new FileNotFoundException(
            "CDB or WinDbg not found. Install Windows SDK or WinDbg from Microsoft Store.\n" +
            "Searched paths:\n" + string.Join("\n", PotentialPaths) +
            (foundPaths.Any() ? "\n\nFound alternatives:\n" + string.Join("\n", foundPaths) : ""));
    }

    public static bool ValidateDebuggerPath(string path, ILogger? logger = null)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        if (!File.Exists(path))
        {
            logger?.LogError("Debugger not found at: {Path}", path);
            return false;
        }

        if (!path.EndsWith("cdb.exe", StringComparison.OrdinalIgnoreCase) && 
            !path.EndsWith("windbg.exe", StringComparison.OrdinalIgnoreCase))
        {
            logger?.LogWarning("Path doesn't appear to be a valid debugger: {Path}", path);
            return false;
        }

        logger?.LogInformation("Validated debugger path: {Path}", path);
        return true;
    }
}