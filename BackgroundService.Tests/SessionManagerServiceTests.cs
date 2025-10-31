using BackgroundService.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace BackgroundService.Tests;

public sealed class SessionManagerServiceTests : IDisposable
{
    private readonly Mock<ILogger<SessionManagerService>> _mockLogger;
    private readonly Mock<ILoggerFactory> _mockLoggerFactory;
    private readonly Mock<IPathDetectionService> _mockPathDetection;
    private readonly Mock<IAnalysisService> _mockAnalysisService;
    private readonly Mock<IJobManagerService> _mockJobManager;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly SessionManagerService _sessionManager;

    public SessionManagerServiceTests()
    {
        _mockLogger = new Mock<ILogger<SessionManagerService>>();
        _mockLoggerFactory = new Mock<ILoggerFactory>();
        _mockPathDetection = new Mock<IPathDetectionService>();
        _mockAnalysisService = new Mock<IAnalysisService>();
        _mockJobManager = new Mock<IJobManagerService>();
        _mockConfiguration = new Mock<IConfiguration>();

        // Setup configuration mock
        _mockConfiguration.Setup(x => x["Debugger:CdbPath"]).Returns((string?)null);
        _mockConfiguration.Setup(x => x["Debugger:SymbolCache"]).Returns((string?)null);
        _mockConfiguration.Setup(x => x["Debugger:SymbolPathExtra"]).Returns("");

        // Setup path detection to return a valid CDB path
        _mockPathDetection.Setup(x => x.DetectDebuggerPaths())
            .Returns(("C:\\cdb.exe", "C:\\windbg.exe", new List<string> { "C:\\cdb.exe" }));

        _mockPathDetection.Setup(x => x.ValidateDebuggerPath(It.IsAny<string>()))
            .Returns(true);

        _mockPathDetection.Setup(x => x.GetBestDebuggerPath())
            .Returns("C:\\cdb.exe");

        _sessionManager = new SessionManagerService(
            _mockLogger.Object,
            _mockLoggerFactory.Object,
            _mockPathDetection.Object,
            _mockAnalysisService.Object,
            _mockJobManager.Object,
            _mockConfiguration.Object);
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
    public void GetActiveSessions_InitiallyEmpty()
    {
        // Act
        var sessions = _sessionManager.GetActiveSessions();

        // Assert
        Assert.Empty(sessions);
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

    [Fact]
    public void CloseSession_InvalidSessionId_ThrowsArgumentException()
    {
        // Arrange
        var invalidSessionId = "nonexistent";

        // Act & Assert
        Assert.Throws<ArgumentException>(
            () => _sessionManager.CloseSession(invalidSessionId));
    }
}