using BackgroundService.Infrastructure.Detection;
using BackgroundService.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace BackgroundService.Tests;

public sealed class SessionManagerServiceTests : IDisposable
{
    private readonly Mock<ILogger<SessionManagerService>> _mockLogger;
    private readonly Mock<ICdbSessionFactory> _mockSessionFactory;
    private readonly Mock<IJobManagerService> _mockJobManager;
    private readonly SessionManagerService _sessionManager;

    public SessionManagerServiceTests()
    {
        _mockLogger = new Mock<ILogger<SessionManagerService>>();
        _mockSessionFactory = new Mock<ICdbSessionFactory>();
        _mockJobManager = new Mock<IJobManagerService>();

        // Setup session factory to return mock session
        var mockSession = new Mock<ICdbSessionService>();
        mockSession.Setup(x => x.SessionId).Returns("test123");
        mockSession.Setup(x => x.IsActive).Returns(true);
        mockSession.Setup(x => x.LoadDumpAsync(It.IsAny<string>(), It.IsAny<IProgress<Shared.Models.ProgressUpdate>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockSessionFactory.Setup(x => x.CreateSession(It.IsAny<string>()))
            .Returns(mockSession.Object);

        _sessionManager = new SessionManagerService(
            _mockLogger.Object,
            _mockSessionFactory.Object,
            _mockJobManager.Object);
    }

    public void Dispose()
    {
        _sessionManager?.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task CreateSessionWithDumpAsync_NonExistentFile_ThrowsFileNotFoundException()
    {
        // Arrange
        var jobId = "test-job-1";
        var dumpPath = @"C:\nonexistent.dmp";

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _sessionManager.CreateSessionWithDumpAsync(jobId, dumpPath));
    }

    [Fact]
    public async Task CreateSessionWithDumpAsync_EmptyPath_ThrowsFileNotFoundException()
    {
        // Arrange
        var jobId = "test-job-2";
        var invalidPath = "";

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _sessionManager.CreateSessionWithDumpAsync(jobId, invalidPath));
    }

    [Fact]
    public async Task ExecuteCommandAsync_InvalidSessionId_ThrowsArgumentException()
    {
        // Arrange
        var jobId = "test-job-3";
        var invalidSessionId = "nonexistent";
        var command = "kb";

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _sessionManager.ExecuteCommandAsync(jobId, invalidSessionId, command));
    }

    [Fact]
    public async Task ExecuteBasicAnalysisAsync_InvalidSessionId_ThrowsArgumentException()
    {
        // Arrange
        var jobId = "test-job-4";
        var invalidSessionId = "nonexistent";

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _sessionManager.ExecuteBasicAnalysisAsync(jobId, invalidSessionId));
    }

    [Fact]
    public async Task ExecutePredefinedAnalysisAsync_InvalidSessionId_ThrowsArgumentException()
    {
        // Arrange
        var jobId = "test-job-5";
        var invalidSessionId = "nonexistent";
        var analysisType = "basic";

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _sessionManager.ExecutePredefinedAnalysisAsync(jobId, invalidSessionId, analysisType));
    }
}