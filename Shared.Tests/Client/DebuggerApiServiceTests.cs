using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using Moq;
using RichardSzalay.MockHttp;
using Shared;
using Shared.Client;
using Shared.Configuration;
using Shared.Models;

namespace Shared.Tests.Client;

public sealed class DebuggerApiServiceTests : IDisposable
{
    private readonly Mock<ILogger<DebuggerApiService>> _mockLogger;
    private readonly Mock<ISignalRClientService> _mockSignalRClient;
    private readonly MockHttpMessageHandler _mockHttp;
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly JsonSerializerOptions _jsonOptions;

    public DebuggerApiServiceTests()
    {
        _mockLogger = new Mock<ILogger<DebuggerApiService>>();
        _mockSignalRClient = new Mock<ISignalRClientService>();
        _mockHttp = new MockHttpMessageHandler();
        _httpClient = _mockHttp.ToHttpClient();
        _baseUrl = "http://localhost:7997";
        _jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        _mockHttp?.Dispose();
    }

    #region Helper Methods

    private static JobStatus CreateJobStatus(
        string jobId,
        JobState state,
        double progress = 0,
        string? message = null,
        string? sessionId = null,
        string? result = null,
        string? error = null,
        JobOperationType operation = JobOperationType.LoadDump,
        JobPhase phase = JobPhase.Queued)
    {
        return new JobStatus(
            jobId,
            sessionId,
            operation,
            state,
            phase,
            progress,
            message,
            DateTime.UtcNow,
            state != JobState.Queued ? DateTime.UtcNow : null,
            state == JobState.Completed || state == JobState.Failed || state == JobState.Cancelled ? DateTime.UtcNow : null,
            null,
            result,
            error);
    }

    private static JobCreatedResponse CreateJobCreatedResponse(string jobId)
    {
        return new JobCreatedResponse(jobId, $"/api/jobs/{jobId}", $"Job {jobId} created");
    }

    private static ProgressNotification CreateProgressNotification(string jobId, double progress, string? message = null, JobPhase phase = JobPhase.ResolvingSymbols)
    {
        return new ProgressNotification(jobId, phase, progress, message, DateTime.UtcNow);
    }

    #endregion

    #region LoadDumpAsync Tests

    [Fact]
    public async Task LoadDumpAsync_ValidPath_CreatesJobAndReturnsSessionId()
    {
        // Arrange
        var dumpPath = Path.GetTempFileName();
        File.WriteAllText(dumpPath, "test dump");
        var jobId = "job123";
        var sessionId = "session456";

        try
        {
            // Mock job creation
            _mockHttp
                .When(HttpMethod.Post, $"{_baseUrl}{ApiEndpoints.LoadDumpAsync}")
                .Respond("application/json", JsonSerializer.Serialize(CreateJobCreatedResponse(jobId), _jsonOptions));

            // Mock job status polling - first running, then completed
            var callCount = 0;
            _mockHttp
                .When(HttpMethod.Get, $"{_baseUrl}/api/jobs/{jobId}")
                .Respond(req =>
                {
                    callCount++;
                    var status = callCount == 1
                        ? CreateJobStatus(jobId, JobState.Running, 50.0, "Loading dump")
                        : CreateJobStatus(jobId, JobState.Completed, 100.0, "Completed", sessionId, sessionId);

                    var json = JsonSerializer.Serialize(status, _jsonOptions);
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
                    };
                });

            var service = new DebuggerApiService(_mockLogger.Object, _httpClient, _mockSignalRClient.Object, _baseUrl);

            // Act
            var result = await service.LoadDumpAsync(dumpPath);

            // Assert
            Assert.Equal(sessionId, result);
            _mockSignalRClient.Verify(x => x.SubscribeToJobProgress(jobId, It.IsAny<Action<ProgressNotification>>()), Times.Once);
            _mockSignalRClient.Verify(x => x.UnsubscribeFromJobProgress(jobId), Times.Once);
        }
        finally
        {
            if (File.Exists(dumpPath))
                File.Delete(dumpPath);
        }
    }

    [Fact]
    public async Task LoadDumpAsync_FileNotFound_ThrowsFileNotFoundException()
    {
        // Arrange
        var dumpPath = "C:\\NonExistent\\dump.dmp";
        var service = new DebuggerApiService(_mockLogger.Object, _httpClient, _mockSignalRClient.Object, _baseUrl);

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(() => service.LoadDumpAsync(dumpPath));
    }

    [Fact]
    public async Task LoadDumpAsync_NullPath_ThrowsArgumentException()
    {
        // Arrange
        var service = new DebuggerApiService(_mockLogger.Object, _httpClient, _mockSignalRClient.Object, _baseUrl);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => service.LoadDumpAsync(null!));
    }

    [Fact]
    public async Task LoadDumpAsync_HttpFailure_ThrowsHttpRequestException()
    {
        // Arrange
        var dumpPath = Path.GetTempFileName();
        File.WriteAllText(dumpPath, "test dump");

        try
        {
            _mockHttp
                .When(HttpMethod.Post, $"{_baseUrl}{ApiEndpoints.LoadDumpAsync}")
                .Respond(HttpStatusCode.InternalServerError);

            var service = new DebuggerApiService(_mockLogger.Object, _httpClient, _mockSignalRClient.Object, _baseUrl);

            // Act & Assert
            await Assert.ThrowsAsync<HttpRequestException>(() => service.LoadDumpAsync(dumpPath));
        }
        finally
        {
            if (File.Exists(dumpPath))
                File.Delete(dumpPath);
        }
    }

    #endregion

    #region ExecuteCommandAsync Tests

    [Fact]
    public async Task ExecuteCommandAsync_ValidCommand_ReturnsResult()
    {
        // Arrange
        var sessionId = "session123";
        var command = "!analyze -v";
        var jobId = "job456";
        var expectedResult = "Analysis output...";

        _mockHttp
            .When(HttpMethod.Post, $"{_baseUrl}{ApiEndpoints.ExecuteCommandAsync}")
            .Respond("application/json", JsonSerializer.Serialize(CreateJobCreatedResponse(jobId), _jsonOptions));

        _mockHttp
            .When(HttpMethod.Get, $"{_baseUrl}/api/jobs/{jobId}")
            .Respond("application/json", JsonSerializer.Serialize(
                CreateJobStatus(jobId, JobState.Completed, 100.0, "Completed", null, expectedResult), _jsonOptions));

        var service = new DebuggerApiService(_mockLogger.Object, _httpClient, _mockSignalRClient.Object, _baseUrl);

        // Act
        var result = await service.ExecuteCommandAsync(sessionId, command);

        // Assert
        Assert.Equal(expectedResult, result);
    }

    [Theory]
    [InlineData(null, "kb")]
    [InlineData("", "kb")]
    [InlineData("   ", "kb")]
    [InlineData("session123", null)]
    [InlineData("session123", "")]
    [InlineData("session123", "   ")]
    public async Task ExecuteCommandAsync_InvalidInputs_ThrowsArgumentException(string? sessionId, string? command)
    {
        // Arrange
        var service = new DebuggerApiService(_mockLogger.Object, _httpClient, _mockSignalRClient.Object, _baseUrl);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => service.ExecuteCommandAsync(sessionId!, command!));
    }

    #endregion

    #region Job Polling Tests

    [Fact]
    public async Task WaitForJobCompletion_FailedJob_ThrowsInvalidOperationException()
    {
        // Arrange
        var dumpPath = Path.GetTempFileName();
        File.WriteAllText(dumpPath, "test dump");
        var jobId = "job123";
        var errorMessage = "CDB process crashed";

        try
        {
            _mockHttp
                .When(HttpMethod.Post, $"{_baseUrl}{ApiEndpoints.LoadDumpAsync}")
                .Respond("application/json", JsonSerializer.Serialize(CreateJobCreatedResponse(jobId), _jsonOptions));

            _mockHttp
                .When(HttpMethod.Get, $"{_baseUrl}/api/jobs/{jobId}")
                .Respond("application/json", JsonSerializer.Serialize(
                    CreateJobStatus(jobId, JobState.Failed, 50.0, "Failed", null, null, errorMessage), _jsonOptions));

            var service = new DebuggerApiService(_mockLogger.Object, _httpClient, _mockSignalRClient.Object, _baseUrl);

            // Act & Assert
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.LoadDumpAsync(dumpPath));
            Assert.Contains(errorMessage, ex.Message);
        }
        finally
        {
            if (File.Exists(dumpPath))
                File.Delete(dumpPath);
        }
    }

    [Fact]
    public async Task WaitForJobCompletion_CancelledJob_ThrowsOperationCanceledException()
    {
        // Arrange
        var dumpPath = Path.GetTempFileName();
        File.WriteAllText(dumpPath, "test dump");
        var jobId = "job123";

        try
        {
            _mockHttp
                .When(HttpMethod.Post, $"{_baseUrl}{ApiEndpoints.LoadDumpAsync}")
                .Respond("application/json", JsonSerializer.Serialize(CreateJobCreatedResponse(jobId), _jsonOptions));

            _mockHttp
                .When(HttpMethod.Get, $"{_baseUrl}/api/jobs/{jobId}")
                .Respond("application/json", JsonSerializer.Serialize(
                    CreateJobStatus(jobId, JobState.Cancelled, 30.0, "Cancelled"), _jsonOptions));

            var service = new DebuggerApiService(_mockLogger.Object, _httpClient, _mockSignalRClient.Object, _baseUrl);

            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(() => service.LoadDumpAsync(dumpPath));
        }
        finally
        {
            if (File.Exists(dumpPath))
                File.Delete(dumpPath);
        }
    }

    #endregion

    #region SignalR Integration Tests

    [Fact]
    public async Task LoadDumpAsync_ForwardsProgressToIProgress()
    {
        // Arrange
        var dumpPath = Path.GetTempFileName();
        File.WriteAllText(dumpPath, "test dump");
        var jobId = "job123";
        var progressNotifications = new List<ProgressNotificationValue>();
        var progress = new Progress<ProgressNotificationValue>(notification => progressNotifications.Add(notification));

        Action<ProgressNotification>? capturedCallback = null;

        _mockSignalRClient
            .Setup(x => x.SubscribeToJobProgress(jobId, It.IsAny<Action<ProgressNotification>>()))
            .Callback<string, Action<ProgressNotification>>((_, callback) => capturedCallback = callback);

        try
        {
            _mockHttp
                .When(HttpMethod.Post, $"{_baseUrl}{ApiEndpoints.LoadDumpAsync}")
                .Respond("application/json", JsonSerializer.Serialize(CreateJobCreatedResponse(jobId), _jsonOptions));

            _mockHttp
                .When(HttpMethod.Get, $"{_baseUrl}/api/jobs/{jobId}")
                .Respond("application/json", JsonSerializer.Serialize(
                    CreateJobStatus(jobId, JobState.Completed, 100.0, "Done", "session123", "session123"), _jsonOptions));

            var service = new DebuggerApiService(_mockLogger.Object, _httpClient, _mockSignalRClient.Object, _baseUrl);

            // Act
            var task = service.LoadDumpAsync(dumpPath, progress);

            // Simulate SignalR progress notifications
            await Task.Delay(50); // Give time for subscription
            capturedCallback?.Invoke(CreateProgressNotification(jobId, 25.0, "Loading symbols"));
            capturedCallback?.Invoke(CreateProgressNotification(jobId, 75.0, "Processing dump"));

            await task;

            // Assert
            Assert.NotEmpty(progressNotifications);
        }
        finally
        {
            if (File.Exists(dumpPath))
                File.Delete(dumpPath);
        }
    }

    #endregion

    #region CheckHealthAsync Tests

    [Fact]
    public async Task CheckHealthAsync_HealthyService_ReturnsTrue()
    {
        // Arrange
        _mockHttp
            .When(HttpMethod.Get, $"{_baseUrl}{ApiEndpoints.Health}")
            .Respond(HttpStatusCode.OK);

        var service = new DebuggerApiService(_mockLogger.Object, _httpClient, _mockSignalRClient.Object, _baseUrl);

        // Act
        var result = await service.CheckHealthAsync();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task CheckHealthAsync_UnhealthyService_ReturnsFalse()
    {
        // Arrange
        _mockHttp
            .When(HttpMethod.Get, $"{_baseUrl}{ApiEndpoints.Health}")
            .Respond(HttpStatusCode.ServiceUnavailable);

        var service = new DebuggerApiService(_mockLogger.Object, _httpClient, _mockSignalRClient.Object, _baseUrl);

        // Act
        var result = await service.CheckHealthAsync();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task CheckHealthAsync_NetworkFailure_ReturnsFalse()
    {
        // Arrange
        _mockHttp
            .When(HttpMethod.Get, $"{_baseUrl}{ApiEndpoints.Health}")
            .Throw(new HttpRequestException("Network error"));

        var service = new DebuggerApiService(_mockLogger.Object, _httpClient, _mockSignalRClient.Object, _baseUrl);

        // Act
        var result = await service.CheckHealthAsync();

        // Assert
        Assert.False(result);
    }

    #endregion
}
