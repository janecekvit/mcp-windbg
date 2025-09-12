using McpProxy.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace McpProxy.Tests;

public class CommunicationServiceTests
{
    private readonly Mock<ILogger<CommunicationService>> _mockLogger;
    private readonly Mock<IToolsService> _mockToolsService;
    private readonly CommunicationService _communicationService;

    public CommunicationServiceTests()
    {
        _mockLogger = new Mock<ILogger<CommunicationService>>();
        _mockToolsService = new Mock<IToolsService>();
        _communicationService = new CommunicationService(_mockLogger.Object, _mockToolsService.Object);
    }

    [Fact]
    public void SendProgressNotificationAsync_WithValidToken_CompletesSuccessfully()
    {
        // Arrange
        var token = "test-token";
        var progress = 0.5;
        var message = "Test message";

        // Act & Assert
        // Method should complete without throwing when writer is null
        var task = _communicationService.SendProgressNotificationAsync(token, progress, message);
        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public void SendProgressNotificationAsync_WithNullMessage_CompletesSuccessfully()
    {
        // Arrange
        var token = "test-token";
        var progress = 1.0;

        // Act & Assert
        var task = _communicationService.SendProgressNotificationAsync(token, progress, null);
        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public void SendProgressNotificationAsync_WithEmptyToken_CompletesSuccessfully()
    {
        // Arrange
        var progress = 0.25;
        var message = "Test";

        // Act & Assert
        var task = _communicationService.SendProgressNotificationAsync("", progress, message);
        Assert.True(task.IsCompletedSuccessfully);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void SendProgressNotificationAsync_WithVariousProgressValues_CompletesSuccessfully(double progress)
    {
        // Arrange
        var token = "test";
        var message = $"Progress: {progress}";

        // Act & Assert
        var task = _communicationService.SendProgressNotificationAsync(token, progress, message);
        Assert.True(task.IsCompletedSuccessfully);
    }
}