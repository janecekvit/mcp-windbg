using BackgroundService.Infrastructure.Detection;
using BackgroundService.Infrastructure.IO;
using Microsoft.Extensions.Logging;
using Moq;

namespace BackgroundService.Tests;

public class PathDetectionServiceTests
{
    private readonly Mock<ILogger<PathDetectionService>> _mockLogger;
    private readonly Mock<ILogger<PathExpansionService>> _mockExpansionLogger;
    private readonly IPathExpansionService _pathExpansionService;
    private readonly PathDetectionService _pathDetectionService;

    public PathDetectionServiceTests()
    {
        _mockLogger = new Mock<ILogger<PathDetectionService>>();
        _mockExpansionLogger = new Mock<ILogger<PathExpansionService>>();
        _pathExpansionService = new PathExpansionService(_mockExpansionLogger.Object);
        _pathDetectionService = new PathDetectionService(_mockLogger.Object, _pathExpansionService);
    }

    [Fact]
    public void DetectDebuggerPaths_ReturnsValidPaths()
    {
        // Act
        var foundPaths = _pathDetectionService.DetectDebuggerPaths();

        // Assert
        // Paths may be null if debuggers are not installed, but method should not throw
        Assert.NotNull(foundPaths);

        // If paths are found, they should be valid
        foreach (var path in foundPaths)
        {
            Assert.True(path.Contains("cdb.exe", StringComparison.OrdinalIgnoreCase) ||
                       path.Contains("cdb", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void DetectDebuggerPaths_ReturnsConsistentResults()
    {
        // Act
        var foundPaths = _pathDetectionService.DetectDebuggerPaths();
        var foundPaths2 = _pathDetectionService.DetectDebuggerPaths();

        // Assert
        Assert.Equal(foundPaths, foundPaths2);
    }

    [Fact]
    public void GetBestDebuggerPath_ReturnsValidPathOrThrowsIfNotFound()
    {
        try
        {
            // Act
            var bestPath = _pathDetectionService.GetBestDebuggerPath();

            // Assert - If path is found, it should be valid
            Assert.NotNull(bestPath);
            Assert.True(bestPath.Contains("cdb.exe", StringComparison.OrdinalIgnoreCase) ||
                       bestPath.Contains("cdb", StringComparison.OrdinalIgnoreCase));
        }
        catch (FileNotFoundException)
        {
            // Expected if CDB is not installed
            Assert.True(true);
        }
    }

    [Fact]
    public void GetBestDebuggerPath_ReturnsConsistentResults()
    {
        try
        {
            // Act
            var bestPath1 = _pathDetectionService.GetBestDebuggerPath();
            var bestPath2 = _pathDetectionService.GetBestDebuggerPath();

            // Assert
            Assert.Equal(bestPath1, bestPath2);
        }
        catch (FileNotFoundException)
        {
            // Expected if CDB is not installed - just verify it's consistent
            Assert.Throws<FileNotFoundException>(() => _pathDetectionService.GetBestDebuggerPath());
        }
    }
}