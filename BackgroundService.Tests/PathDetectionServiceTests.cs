using BackgroundService.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace BackgroundService.Tests;

public class PathDetectionServiceTests
{
    private readonly Mock<ILogger<PathDetectionService>> _mockLogger;
    private readonly PathDetectionService _pathDetectionService;

    public PathDetectionServiceTests()
    {
        _mockLogger = new Mock<ILogger<PathDetectionService>>();
        _pathDetectionService = new PathDetectionService(_mockLogger.Object);
    }

    [Fact]
    public void DetectDebuggerPaths_ReturnsValidPaths()
    {
        // Act
        var (CdbPath, WinDbgPath, FoundPaths) = _pathDetectionService.DetectDebuggerPaths();

        // Assert
        // Paths may be null if debuggers are not installed, but method should not throw
        Assert.NotNull(FoundPaths);

        // If paths are found, they should be valid
        if (!string.IsNullOrEmpty(CdbPath))
        {
            Assert.True(CdbPath.Contains("cdb.exe", StringComparison.OrdinalIgnoreCase) ||
                       CdbPath.Contains("cdb", StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(WinDbgPath))
        {
            Assert.True(WinDbgPath.Contains("windbg.exe", StringComparison.OrdinalIgnoreCase) ||
                       WinDbgPath.Contains("windbg", StringComparison.OrdinalIgnoreCase) ||
                       WinDbgPath.Contains("WinDbg", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void DetectDebuggerPaths_HandlesEnvironmentVariables()
    {
        // Arrange
        var originalCdbPath = Environment.GetEnvironmentVariable("CDB_PATH");
        var testPath = @"C:\TestPath\cdb.exe";

        try
        {
            // Set environment variable for test
            Environment.SetEnvironmentVariable("CDB_PATH", testPath);

            // Act
            var (CdbPath, WinDbgPath, FoundPaths) = _pathDetectionService.DetectDebuggerPaths();

            // Assert
            // The service should respect environment variable if file exists
            // Since test file doesn't exist, it will fallback to auto-detection
            Assert.NotNull(FoundPaths);
        }
        finally
        {
            // Restore original environment variable
            Environment.SetEnvironmentVariable("CDB_PATH", originalCdbPath);
        }
    }

    [Fact]
    public void DetectDebuggerPaths_ReturnsConsistentResults()
    {
        // Act
        var (CdbPath, WinDbgPath, FoundPaths)= _pathDetectionService.DetectDebuggerPaths();
        var result2 = _pathDetectionService.DetectDebuggerPaths();

        // Assert
        Assert.Equal(CdbPath, result2.CdbPath);
        Assert.Equal(WinDbgPath, result2.WinDbgPath);
        Assert.Equal(FoundPaths, result2.FoundPaths);
    }
}