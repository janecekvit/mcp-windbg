using BackgroundService.Services;
using Common;

namespace BackgroundService.Tests;

public class AnalysisServiceTests
{
    private readonly AnalysisService _analysisService;

    public AnalysisServiceTests()
    {
        _analysisService = new AnalysisService();
    }

    [Fact]
    public void GetAvailableAnalyses_ReturnsExpectedAnalyses()
    {
        // Act
        var analyses = _analysisService.GetAvailableAnalyses();

        // Assert
        Assert.NotEmpty(analyses);
        Assert.Contains("basic", analyses);
        Assert.Contains("exception", analyses);
        Assert.Contains("threads", analyses);
        Assert.Contains("heap", analyses);
        Assert.Contains("modules", analyses);
        Assert.Contains("handles", analyses);
        Assert.Contains("locks", analyses);
        Assert.Contains("memory", analyses);
        Assert.Contains("drivers", analyses);
        Assert.Contains("processes", analyses);
        Assert.Equal(10, analyses.Count());
    }

    [Theory]
    [InlineData("basic")]
    [InlineData("exception")]
    [InlineData("threads")]
    [InlineData("heap")]
    [InlineData("modules")]
    public void GetAnalysisDescription_ValidAnalysisType_ReturnsDescription(string analysisType)
    {
        // Arrange
        var expectedDescription = AnalysisTypeExtensions.FromIdentifier(analysisType).GetDescription();
        
        // Act
        var description = _analysisService.GetAnalysisDescription(analysisType);

        // Assert
        Assert.Equal(expectedDescription, description);
        Assert.NotEmpty(description);
    }

    [Fact]
    public void GetAnalysisDescription_InvalidAnalysisType_ReturnsNotFoundMessage()
    {
        // Arrange
        var invalidType = "nonexistent";

        // Act
        var description = _analysisService.GetAnalysisDescription(invalidType);

        // Assert
        Assert.Equal("Unknown analysis type", description);
    }

    [Theory]
    [InlineData("basic")]
    [InlineData("exception")]
    [InlineData("threads")]
    [InlineData("heap")]
    [InlineData("modules")]
    [InlineData("handles")]
    [InlineData("locks")]
    [InlineData("memory")]
    [InlineData("drivers")]
    [InlineData("processes")]
    public void GetAnalysisCommands_ValidAnalysisType_ReturnsCommands(string analysisType)
    {
        // Act
        var commands = _analysisService.GetAnalysisCommands(analysisType);

        // Assert
        Assert.NotEmpty(commands);
        Assert.All(commands, cmd => Assert.False(string.IsNullOrWhiteSpace(cmd)));
    }

    [Fact]
    public void GetAnalysisCommands_InvalidAnalysisType_ReturnsEmptyArray()
    {
        // Arrange
        var invalidType = "nonexistent";

        // Act
        var commands = _analysisService.GetAnalysisCommands(invalidType);

        // Assert
        Assert.Empty(commands);
    }
}