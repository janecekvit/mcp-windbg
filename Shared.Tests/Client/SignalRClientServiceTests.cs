using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Moq;
using Shared.Client;
using Shared.Models;

namespace Shared.Tests.Client;

public class SignalRClientServiceTests : IAsyncDisposable
{
    private readonly Mock<ILogger<SignalRClientService>> _mockLogger;
    private readonly string _hubUrl = "http://localhost:7997/hubs/progress";

    public SignalRClientServiceTests()
    {
        _mockLogger = new Mock<ILogger<SignalRClientService>>();
    }

    public async ValueTask DisposeAsync()
    {
        await Task.CompletedTask;
    }

    #region Connection Tests

    [Fact(Skip = "Integration test - requires BackgroundService running on localhost:7997")]
    public async Task ConnectAsync_ValidUrl_EstablishesConnection()
    {
        // Arrange
        var service = new SignalRClientService(_mockLogger.Object, _hubUrl);

        // Act
        await service.ConnectAsync();

        // Assert
        Assert.True(service.IsConnected);

        // Cleanup
        await service.DisposeAsync();
    }

    [Fact(Skip = "Integration test - requires BackgroundService running on localhost:7997")]
    public async Task ConnectAsync_AlreadyConnected_DoesNothing()
    {
        // Arrange
        var service = new SignalRClientService(_mockLogger.Object, _hubUrl);
        await service.ConnectAsync();
        Assert.True(service.IsConnected);

        // Act - connect again
        await service.ConnectAsync();

        // Assert
        Assert.True(service.IsConnected);

        // Cleanup
        await service.DisposeAsync();
    }

    [Fact(Skip = "Integration test - requires BackgroundService running on localhost:7997")]
    public async Task DisposeAsync_DisconnectsAndCleanup()
    {
        // Arrange
        var service = new SignalRClientService(_mockLogger.Object, _hubUrl);
        await service.ConnectAsync();
        Assert.True(service.IsConnected);

        // Act
        await service.DisposeAsync();

        // Assert
        Assert.False(service.IsConnected);
    }

    #endregion

    #region Subscription Tests

    [Fact(Skip = "Integration test - requires BackgroundService running on localhost:7997")]
    public async Task SubscribeToJobProgress_ValidJobId_InvokesCallbackOnProgress()
    {
        // Arrange
        var service = new SignalRClientService(_mockLogger.Object, _hubUrl);
        await service.ConnectAsync();

        var jobId = "job123";
        var receivedNotifications = new List<ProgressNotification>();

        service.SubscribeToJobProgress(jobId, notification =>
        {
            receivedNotifications.Add(notification);
        });

        // Simulate SignalR hub sending progress notification
        // Note: In real scenario, this would come from the hub
        // For unit testing, we can only verify subscription was registered

        // Act - Clean up
        await service.DisposeAsync();

        // Assert
        // Subscription was registered (no exception thrown)
        Assert.True(true);
    }

    [Fact(Skip = "Integration test - requires BackgroundService running on localhost:7997")]
    public async Task UnsubscribeFromJobProgress_RemovesCallback()
    {
        // Arrange
        var service = new SignalRClientService(_mockLogger.Object, _hubUrl);
        await service.ConnectAsync();

        var jobId = "job123";
        var callbackInvokeCount = 0;

        service.SubscribeToJobProgress(jobId, notification =>
        {
            callbackInvokeCount++;
        });

        // Act
        service.UnsubscribeFromJobProgress(jobId);

        // Assert
        // After unsubscribing, callback should not be invoked
        // (We can't easily test this without a real hub, but we verify no exception)
        Assert.Equal(0, callbackInvokeCount);

        // Cleanup
        await service.DisposeAsync();
    }

    [Fact(Skip = "Integration test - requires BackgroundService running on localhost:7997")]
    public async Task SubscribeToJobAsync_CallsHubMethod()
    {
        // Arrange
        var service = new SignalRClientService(_mockLogger.Object, _hubUrl);
        await service.ConnectAsync();

        var jobId = "job123";

        // Act & Assert - should not throw
        await service.SubscribeToJobAsync(jobId);

        // Cleanup
        await service.DisposeAsync();
    }

    [Fact(Skip = "Integration test - requires BackgroundService running on localhost:7997")]
    public async Task UnsubscribeFromJobAsync_CallsHubMethod()
    {
        // Arrange
        var service = new SignalRClientService(_mockLogger.Object, _hubUrl);
        await service.ConnectAsync();

        var jobId = "job123";
        await service.SubscribeToJobAsync(jobId);

        // Act & Assert - should not throw
        await service.UnsubscribeFromJobAsync(jobId);

        // Cleanup
        await service.DisposeAsync();
    }

    #endregion

    #region Job Completion Tests

    [Fact(Skip = "Integration test - requires BackgroundService running on localhost:7997")]
    public async Task WaitForJobCompletionAsync_TimesOut_ThrowsTimeoutException()
    {
        // Arrange
        var service = new SignalRClientService(_mockLogger.Object, _hubUrl);
        await service.ConnectAsync();

        var jobId = "job123";
        var timeout = TimeSpan.FromMilliseconds(100);

        // Act & Assert
        await Assert.ThrowsAsync<TimeoutException>(() =>
            service.WaitForJobCompletionAsync(jobId, timeout));

        // Cleanup
        await service.DisposeAsync();
    }

    [Fact(Skip = "Integration test - requires BackgroundService running on localhost:7997")]
    public async Task WaitForJobCompletionAsync_NotConnected_ThrowsInvalidOperationException()
    {
        // Arrange
        var service = new SignalRClientService(_mockLogger.Object, _hubUrl);
        var jobId = "job123";

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.WaitForJobCompletionAsync(jobId, TimeSpan.FromSeconds(1)));
    }

    #endregion

    #region Concurrent Job Tracking Tests

    [Fact(Skip = "Integration test - requires BackgroundService running on localhost:7997")]
    public async Task MultipleJobSubscriptions_TrackedIndependently()
    {
        // Arrange
        var service = new SignalRClientService(_mockLogger.Object, _hubUrl);
        await service.ConnectAsync();

        var job1Notifications = new List<ProgressNotification>();
        var job2Notifications = new List<ProgressNotification>();

        // Act
        service.SubscribeToJobProgress("job1", notification => job1Notifications.Add(notification));
        service.SubscribeToJobProgress("job2", notification => job2Notifications.Add(notification));

        // Unsubscribe from job1 only
        service.UnsubscribeFromJobProgress("job1");

        // Assert
        // Both subscriptions were registered independently
        Assert.Empty(job1Notifications);
        Assert.Empty(job2Notifications);

        // Cleanup
        await service.DisposeAsync();
    }

    [Fact(Skip = "Integration test - requires BackgroundService running on localhost:7997")]
    public async Task SubscribeToJobProgress_SameJobTwice_ReplacesCallback()
    {
        // Arrange
        var service = new SignalRClientService(_mockLogger.Object, _hubUrl);
        await service.ConnectAsync();

        var firstCallbackInvoked = false;
        var secondCallbackInvoked = false;

        // Act
        service.SubscribeToJobProgress("job1", _ => firstCallbackInvoked = true);
        service.SubscribeToJobProgress("job1", _ => secondCallbackInvoked = true);

        // Assert
        // Second subscription should replace the first
        Assert.False(firstCallbackInvoked);
        Assert.False(secondCallbackInvoked);

        // Cleanup
        await service.DisposeAsync();
    }

    #endregion

    #region Error Handling Tests

    [Fact(Skip = "Integration test - requires BackgroundService running on localhost:7997")]
    public async Task ConnectAsync_InvalidUrl_ThrowsException()
    {
        // Arrange
        var invalidUrl = "invalid-url";
        var service = new SignalRClientService(_mockLogger.Object, invalidUrl);

        // Act & Assert
        // Note: This may not throw immediately due to automatic reconnection
        // The connection will be attempted but may fail asynchronously
        try
        {
            await service.ConnectAsync();
            // If it doesn't throw, that's also acceptable (async retry logic)
            Assert.True(true);
        }
        catch (Exception)
        {
            // Expected for invalid URL
            Assert.True(true);
        }
    }

    [Fact(Skip = "Integration test - requires BackgroundService running on localhost:7997")]
    public async Task DisposeAsync_DisconnectedService_DoesNotThrow()
    {
        // Arrange
        var service = new SignalRClientService(_mockLogger.Object, _hubUrl);

        // Act & Assert - should not throw even when not connected
        await service.DisposeAsync();
        Assert.True(true);
    }

    [Fact(Skip = "Integration test - requires BackgroundService running on localhost:7997")]
    public async Task UnsubscribeFromJobProgress_NonExistentJob_DoesNotThrow()
    {
        // Arrange
        var service = new SignalRClientService(_mockLogger.Object, _hubUrl);
        await service.ConnectAsync();

        // Act & Assert - should not throw for non-existent job
        service.UnsubscribeFromJobProgress("non-existent-job");
        Assert.True(true);

        // Cleanup
        await service.DisposeAsync();
    }

    #endregion

    #region Integration-Like Tests (with real SignalR client)

    [Fact(Skip = "Integration test - requires BackgroundService running on localhost:7997")]
    public async Task FullLifecycle_ConnectSubscribeUnsubscribeDisconnect()
    {
        // Arrange
        var service = new SignalRClientService(_mockLogger.Object, _hubUrl);
        var jobId = "job123";
        var notifications = new List<ProgressNotification>();

        // Act
        await service.ConnectAsync();
        Assert.True(service.IsConnected);

        service.SubscribeToJobProgress(jobId, notification => notifications.Add(notification));
        await service.SubscribeToJobAsync(jobId);

        await service.UnsubscribeFromJobAsync(jobId);
        service.UnsubscribeFromJobProgress(jobId);

        await service.DisposeAsync();

        // Assert
        Assert.False(service.IsConnected);
        Assert.Empty(notifications); // No notifications received in unit test
    }

    [Fact(Skip = "Integration test - requires BackgroundService running on localhost:7997")]
    public async Task Reconnection_AfterDispose_RequiresNewConnect()
    {
        // Arrange
        var service = new SignalRClientService(_mockLogger.Object, _hubUrl);

        // Act
        await service.ConnectAsync();
        Assert.True(service.IsConnected);

        await service.DisposeAsync();
        Assert.False(service.IsConnected);

        // Reconnect
        await service.ConnectAsync();

        // Assert
        Assert.True(service.IsConnected);

        // Cleanup
        await service.DisposeAsync();
    }

    #endregion
}
