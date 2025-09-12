using System.Net;
using System.Text.Json;
using McpProxy.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;

namespace McpProxy.Tests;

public sealed class DebuggerApiServiceTests : IDisposable
{
    private readonly Mock<ILogger<DebuggerApiService>> _mockLogger;
    private readonly Mock<ICommunicationService> _mockCommunicationService;
    private readonly Mock<HttpMessageHandler> _mockHttpMessageHandler;
    private readonly HttpClient _httpClient;
    private readonly DebuggerApiService _debuggerApiService;

    public DebuggerApiServiceTests()
    {
        _mockLogger = new Mock<ILogger<DebuggerApiService>>();
        _mockCommunicationService = new Mock<ICommunicationService>();
        _mockHttpMessageHandler = new Mock<HttpMessageHandler>();

        // Setup HttpClient to return failure for health check
        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        _httpClient = new HttpClient(_mockHttpMessageHandler.Object);

        _debuggerApiService = new DebuggerApiService(_mockLogger.Object, _httpClient, _mockCommunicationService.Object);
    }

    [Fact]
    public async Task CheckHealthAsync_WithUnreachableService_ReturnsFalse()
    {
        // Act
        var result = await _debuggerApiService.CheckHealthAsync();

        // Assert
        // Should return false when background service is not running
        Assert.False(result);
    }

    [Fact]
    public async Task LoadDumpAsync_WithMissingParameter_ReturnsError()
    {
        // Arrange
        var args = JsonDocument.Parse("{}").RootElement;

        // Act
        var result = await _debuggerApiService.LoadDumpAsync(args);

        // Assert
        Assert.True(result.IsError);
        Assert.Equal("Missing dump_file_path parameter", result.Content[0].Text);
    }

    [Fact]
    public async Task LoadDumpAsync_WithEmptyPath_ReturnsError()
    {
        // Arrange
        var args = JsonDocument.Parse("{\"dump_file_path\": \"\"}").RootElement;

        // Act
        var result = await _debuggerApiService.LoadDumpAsync(args);

        // Assert
        Assert.True(result.IsError);
        Assert.Contains("Dump file path is required", result.Content[0].Text);
    }

    [Fact]
    public async Task ExecuteCommandAsync_WithMissingSessionId_ReturnsError()
    {
        // Arrange
        var args = JsonDocument.Parse("{\"command\": \"kb\"}").RootElement;

        // Act
        var result = await _debuggerApiService.ExecuteCommandAsync(args);

        // Assert
        Assert.True(result.IsError);
        Assert.Equal("Missing session_id or command parameter", result.Content[0].Text);
    }

    [Fact]
    public async Task ExecuteCommandAsync_WithMissingCommand_ReturnsError()
    {
        // Arrange
        var args = JsonDocument.Parse("{\"session_id\": \"test123\"}").RootElement;

        // Act
        var result = await _debuggerApiService.ExecuteCommandAsync(args);

        // Assert
        Assert.True(result.IsError);
        Assert.Equal("Missing session_id or command parameter", result.Content[0].Text);
    }

    [Fact]
    public async Task BasicAnalysisAsync_WithMissingSessionId_ReturnsError()
    {
        // Arrange
        var args = JsonDocument.Parse("{}").RootElement;

        // Act
        var result = await _debuggerApiService.BasicAnalysisAsync(args);

        // Assert
        Assert.True(result.IsError);
        Assert.Equal("Missing session_id parameter", result.Content[0].Text);
    }

    [Fact]
    public async Task PredefinedAnalysisAsync_WithMissingParameters_ReturnsError()
    {
        // Arrange
        var args = JsonDocument.Parse("{\"session_id\": \"test123\"}").RootElement;

        // Act
        var result = await _debuggerApiService.PredefinedAnalysisAsync(args);

        // Assert
        Assert.True(result.IsError);
        Assert.Equal("Missing session_id or analysis_type parameter", result.Content[0].Text);
    }

    [Fact]
    public async Task CloseSessionAsync_WithMissingSessionId_ReturnsError()
    {
        // Arrange
        var args = JsonDocument.Parse("{}").RootElement;

        // Act
        var result = await _debuggerApiService.CloseSessionAsync(args);

        // Assert
        Assert.True(result.IsError);
        Assert.Equal("Missing session_id parameter", result.Content[0].Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task LoadDumpAsync_WithInvalidPath_ReturnsValidationError(string? invalidPath)
    {
        // Arrange
        var jsonContent = invalidPath == null
            ? "{\"dump_file_path\": null}"
            : $"{{\"dump_file_path\": \"{invalidPath}\"}}";
        var args = JsonDocument.Parse(jsonContent).RootElement;

        // Act
        var result = await _debuggerApiService.LoadDumpAsync(args);

        // Assert
        Assert.True(result.IsError);
        Assert.Contains("required", result.Content[0].Text);
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        GC.SuppressFinalize(this);
    }
}