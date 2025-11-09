using BackgroundService.Infrastructure.IO;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace BackgroundService.Tests;

/// <summary>
/// Tests for general-purpose path expansion service with wildcard support
/// </summary>
public class PathExpansionServiceTests
{
    private readonly ITestOutputHelper _output;
    private readonly Mock<ILogger<PathExpansionService>> _mockLogger;
    private readonly PathExpansionService _pathExpansionService;

    public PathExpansionServiceTests(ITestOutputHelper output)
    {
        _output = output;
        _mockLogger = new Mock<ILogger<PathExpansionService>>();
        _pathExpansionService = new PathExpansionService(_mockLogger.Object);
    }

    [Fact]
    public void ExpandWildcardPath_WithProgramFilesPattern_FindsBothVariants()
    {
        // Arrange
        var pattern = @"C:\Program Files*";

        // Act
        var paths = _pathExpansionService.ExpandWildcardPath(pattern).ToList();

        // Output
        _output.WriteLine($"Pattern: {pattern}");
        _output.WriteLine($"Found {paths.Count} paths:");
        foreach (var path in paths)
        {
            _output.WriteLine($"  - {path}");
        }

        // Assert
        Assert.NotNull(paths);

        // On most Windows systems, at least one of these should exist
        if (Directory.Exists(@"C:\Program Files") || Directory.Exists(@"C:\Program Files (x86)"))
        {
            Assert.NotEmpty(paths);

            // All found paths should start with "C:\Program Files"
            foreach (var path in paths)
            {
                Assert.StartsWith(@"C:\Program Files", path, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void ExpandWildcardPath_WithFilePattern_FindsExecutables()
    {
        // Arrange
        var pattern = @"C:\Windows\System32\*.exe";

        // Act
        var paths = _pathExpansionService.ExpandWildcardPath(pattern).ToList();

        // Output
        _output.WriteLine($"Pattern: {pattern}");
        _output.WriteLine($"Found {paths.Count} exe files in System32");
        _output.WriteLine($"First 5 examples:");
        foreach (var path in paths.Take(5))
        {
            _output.WriteLine($"  - {path}");
        }

        // Assert
        Assert.NotNull(paths);

        // C:\Windows\System32 should exist on all Windows systems
        if (Directory.Exists(@"C:\Windows\System32"))
        {
            Assert.NotEmpty(paths);

            // All found paths should be .exe files in System32
            foreach (var path in paths)
            {
                Assert.EndsWith(".exe", path, StringComparison.OrdinalIgnoreCase);
                Assert.Contains(@"System32", path);
            }
        }
    }

    [Fact]
    public void ExpandWildcardPath_WithNestedWildcards_WorksCorrectly()
    {
        // Arrange - pattern that should find Windows SDK debuggers (if installed)
        var pattern = @"C:\Program Files*\Windows Kits\*\Debuggers\*\cdb.exe";

        // Act
        var paths = _pathExpansionService.ExpandWildcardPath(pattern).ToList();

        // Output
        _output.WriteLine($"Pattern: {pattern}");
        _output.WriteLine($"Found {paths.Count} paths:");
        foreach (var path in paths)
        {
            _output.WriteLine($"  - {path}");
        }

        // Assert
        Assert.NotNull(paths);

        // If found, all should be cdb.exe in Debuggers folder
        foreach (var path in paths)
        {
            Assert.EndsWith("cdb.exe", path, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Debuggers", path);
        }
    }

    [Fact]
    public void ExpandWildcardPath_WithNonExistentPath_ReturnsEmpty()
    {
        // Arrange
        var pattern = @"C:\NonExistentFolder\*.txt";

        // Act
        var paths = _pathExpansionService.ExpandWildcardPath(pattern).ToList();

        // Output
        _output.WriteLine($"Pattern: {pattern}");
        _output.WriteLine($"Found {paths.Count} paths (expected 0)");

        // Assert
        Assert.NotNull(paths);
        Assert.Empty(paths);
    }

    [Fact]
    public void ExpandWildcardPath_WithEmptyPattern_ReturnsEmpty()
    {
        // Arrange
        var pattern = "";

        // Act
        var paths = _pathExpansionService.ExpandWildcardPath(pattern).ToList();

        // Assert
        Assert.NotNull(paths);
        Assert.Empty(paths);
    }

    [Fact]
    public void ExpandWildcardPath_WithDirectoryPattern_FindsDirectories()
    {
        // Arrange - find all subdirectories in Windows folder
        var pattern = @"C:\Windows\*";

        // Act
        var paths = _pathExpansionService.ExpandWildcardPath(pattern).ToList();

        // Output
        _output.WriteLine($"Pattern: {pattern}");
        _output.WriteLine($"Found {paths.Count} subdirectories in C:\\Windows");
        _output.WriteLine($"First 10 examples:");
        foreach (var path in paths.Take(10))
        {
            _output.WriteLine($"  - {path}");
        }

        // Assert
        Assert.NotNull(paths);

        if (Directory.Exists(@"C:\Windows"))
        {
            Assert.NotEmpty(paths);

            // All should be directories in C:\Windows
            foreach (var path in paths)
            {
                Assert.True(Directory.Exists(path), $"Path should be a directory: {path}");
                Assert.StartsWith(@"C:\Windows\", path, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
