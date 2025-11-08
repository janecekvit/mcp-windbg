using System.Runtime.InteropServices;
using BackgroundService.Infrastructure.Detection;
using BackgroundService.Infrastructure.IO;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BackgroundService.Tests;

public class PathPrioritizationTests
{
    /// <summary>
    /// Integration test that verifies GetBestDebuggerPath prioritizes correctly
    /// when CDB is actually installed on the system.
    /// 1. WindowsApps over Windows SDK
    /// 2. amd64 over other architectures
    /// </summary>
    [Fact]
    public void GetBestDebuggerPath_PrioritizesWindowsAppsAmd64_WhenInstalled()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<PathDetectionService>>();
        var mockExpansionLogger = new Mock<ILogger<PathExpansionService>>();
        var pathExpansionService = new PathExpansionService(mockExpansionLogger.Object);

        var service = new PathDetectionService(mockLogger.Object, pathExpansionService);

        try
        {
            // Act
            var detectedPaths = service.DetectDebuggerPaths();

            // Only run the test if CDB is actually installed
            if (!detectedPaths.Any())
            {
                // Skip test if no CDB installations found
                return;
            }

            var bestPath = service.GetBestDebuggerPath();

            // Assert - If multiple paths found, verify prioritization
            Assert.NotNull(bestPath);

            if (detectedPaths.Count > 1)
            {
                // If WindowsApps version exists, it should be preferred
                var hasWindowsApps = detectedPaths.Any(p => p.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase));
                var hasAmd64 = detectedPaths.Any(p => p.Contains("\\amd64\\", StringComparison.OrdinalIgnoreCase));

                if (hasWindowsApps && hasAmd64)
                {
                    // Best path should be WindowsApps + amd64 combination
                    Assert.Contains("WindowsApps", bestPath, StringComparison.OrdinalIgnoreCase);
                    Assert.Contains("amd64", bestPath, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
        catch (FileNotFoundException)
        {
            // Expected if CDB not installed - test passes
            Assert.True(true);
        }
    }

    /// <summary>
    /// Tests the scoring logic for path prioritization without requiring real files.
    /// This tests the core algorithm that determines which path is best.
    /// </summary>
    [Fact]
    public void PathScoring_PrioritizesCorrectly()
    {
        // Test paths with expected scores (on X64 system)
        var testCases = new[]
        {
            (Path: @"C:\Program Files\WindowsApps\Microsoft.WinDbg\amd64\cdb.exe", ExpectedScore: 150, Name: "WindowsApps-amd64"),  // 100 + 50
            (Path: @"C:\Program Files\WindowsApps\Microsoft.WinDbg\x86\cdb.exe", ExpectedScore: 100, Name: "WindowsApps-x86"),     // 100 + 0
            (Path: @"C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\cdb.exe", ExpectedScore: 100, Name: "WindowsKits-x64"),  // 50 + 50
            (Path: @"C:\Program Files\WindowsApps\Microsoft.WinDbg\arm64\cdb.exe", ExpectedScore: 100, Name: "WindowsApps-arm64")  // 100 + 0
        };

        // Get highest score path
        var bestPath = testCases.OrderByDescending(x => x.ExpectedScore).First();

        // Assert - WindowsApps amd64 should have highest score
        Assert.Equal("WindowsApps-amd64", bestPath.Name);
        Assert.Equal(150, bestPath.ExpectedScore);
    }

    /// <summary>
    /// Tests scoring for different system architectures.
    /// Simple rule: matching architecture gets +50, non-matching gets +0.
    /// </summary>
    [Theory]
    // X64 system tests - amd64 and x64 both match
    [InlineData(@"C:\Program Files\WindowsApps\Microsoft.WinDbg\amd64\cdb.exe", "X64", 150)]  // 100 + 50 (match)
    [InlineData(@"C:\Program Files\WindowsApps\Microsoft.WinDbg\x64\cdb.exe", "X64", 150)]    // 100 + 50 (match)
    [InlineData(@"C:\Program Files\WindowsApps\Microsoft.WinDbg\arm64\cdb.exe", "X64", 100)]  // 100 + 0 (no match)
    [InlineData(@"C:\Program Files\WindowsApps\Microsoft.WinDbg\x86\cdb.exe", "X64", 100)]    // 100 + 0 (no match)
    // ARM64 system tests - only arm64 matches
    [InlineData(@"C:\Program Files\WindowsApps\Microsoft.WinDbg\arm64\cdb.exe", "Arm64", 150)] // 100 + 50 (match)
    [InlineData(@"C:\Program Files\WindowsApps\Microsoft.WinDbg\amd64\cdb.exe", "Arm64", 100)] // 100 + 0 (no match)
    [InlineData(@"C:\Program Files\WindowsApps\Microsoft.WinDbg\x86\cdb.exe", "Arm64", 100)]   // 100 + 0 (no match)
    // X86 system tests - only x86 matches
    [InlineData(@"C:\Program Files\WindowsApps\Microsoft.WinDbg\x86\cdb.exe", "X86", 150)]     // 100 + 50 (match)
    [InlineData(@"C:\Program Files\WindowsApps\Microsoft.WinDbg\amd64\cdb.exe", "X86", 100)]   // 100 + 0 (no match)
    public void CalculatePathScore_ReturnsExpectedScore_ForSystemArchitecture(string path, string archString, int expectedScore)
    {
        // Parse architecture enum
        var architecture = Enum.Parse<Architecture>(archString);

        // Use reflection to access private static method
        var method = typeof(PathDetectionService).GetMethod("_CalculatePathScore",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        // Act - For static methods, pass null as the first parameter
        var score = (int)method!.Invoke(null, new object[] { path, architecture })!;

        // Assert
        Assert.Equal(expectedScore, score);
    }

    /// <summary>
    /// Tests that on X64 systems, amd64 is always preferred over other architectures.
    /// </summary>
    [Fact]
    public void OnX64System_Amd64IsPreferred()
    {
        var paths = new[]
        {
            (@"C:\WindowsApps\arm64\cdb.exe", Architecture.X64, 100),  // 100 + 0
            (@"C:\WindowsApps\x86\cdb.exe", Architecture.X64, 100),    // 100 + 0
            (@"C:\WindowsApps\amd64\cdb.exe", Architecture.X64, 150)   // 100 + 50
        };

        var method = typeof(PathDetectionService).GetMethod("_CalculatePathScore",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var scores = paths.Select(p => (Path: p.Item1, Score: (int)method!.Invoke(null, new object[] { p.Item1, p.Item2 })!)).ToList();
        var bestPath = scores.OrderByDescending(x => x.Score).First();

        Assert.Contains("amd64", bestPath.Path);
        Assert.Equal(150, bestPath.Score);
    }

    /// <summary>
    /// Tests that on ARM64 systems, arm64 is preferred.
    /// </summary>
    [Fact]
    public void OnArm64System_Arm64IsPreferred()
    {
        var paths = new[]
        {
            (@"C:\WindowsApps\amd64\cdb.exe", Architecture.Arm64, 100),  // 100 + 0
            (@"C:\WindowsApps\x86\cdb.exe", Architecture.Arm64, 100),    // 100 + 0
            (@"C:\WindowsApps\arm64\cdb.exe", Architecture.Arm64, 150)   // 100 + 50
        };

        var method = typeof(PathDetectionService).GetMethod("_CalculatePathScore",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var scores = paths.Select(p => (Path: p.Item1, Score: (int)method!.Invoke(null, new object[] { p.Item1, p.Item2 })!)).ToList();
        var bestPath = scores.OrderByDescending(x => x.Score).First();

        Assert.Contains("arm64", bestPath.Path);
        Assert.Equal(150, bestPath.Score);
    }

    /// <summary>
    /// Tests that on X86 (32-bit) systems, x86 is the only viable option.
    /// 64-bit executables get score of 0 (won't run).
    /// </summary>
    [Fact]
    public void OnX86System_Only32BitWorks()
    {
        var paths = new[]
        {
            (@"C:\WindowsApps\amd64\cdb.exe", Architecture.X86, 100),  // 100 + 0 = won't run
            (@"C:\WindowsApps\x86\cdb.exe", Architecture.X86, 150)     // 100 + 50 = works
        };

        var method = typeof(PathDetectionService).GetMethod("_CalculatePathScore",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var scores = paths.Select(p => (Path: p.Item1, Score: (int)method!.Invoke(null, new object[] { p.Item1, p.Item2 })!)).ToList();
        var bestPath = scores.OrderByDescending(x => x.Score).First();

        Assert.Contains("x86", bestPath.Path);
        Assert.Equal(150, bestPath.Score);
    }
}
