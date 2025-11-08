using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Shared;
using Shared.Models;

namespace Shared.Client;

public class SignalRClientService : ISignalRClientService
{
    private readonly ILogger<SignalRClientService> _logger;
    private readonly string _hubUrl;
    private HubConnection? _connection;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JobCompletedNotification>> _jobCompletions = new();
    private readonly ConcurrentDictionary<string, Action<ProgressNotification>> _progressCallbacks = new();

    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    public SignalRClientService(
        ILogger<SignalRClientService> logger,
        string hubUrl = Constants.Network.DefaultProgressHubUrl)
    {
        _logger = logger;
        _hubUrl = hubUrl;
    }

    public void SubscribeToJobProgress(string jobId, Action<ProgressNotification> progressCallback)
    {
        _progressCallbacks[jobId] = progressCallback;
        _logger.LogDebug("Subscribed to progress for job {JobId}", jobId);
    }

    public void UnsubscribeFromJobProgress(string jobId)
    {
        _progressCallbacks.TryRemove(jobId, out _);
        _logger.LogDebug("Unsubscribed from progress for job {JobId}", jobId);
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_connection != null && IsConnected)
        {
            _logger.LogDebug("SignalR client already connected");
            return;
        }

        _logger.LogInformation("Connecting to SignalR hub: {HubUrl}", _hubUrl);

        _connection = new HubConnectionBuilder()
            .WithUrl(_hubUrl)
            .WithAutomaticReconnect()
            .Build();

        // Register handlers for progress notifications
        _connection.On<ProgressNotification>("Progress", (notification) =>
        {
            _logger.LogDebug("Received progress notification for job {JobId}: {Progress:P0} - {Message}",
                notification.JobId, notification.Progress, notification.Message);

            // Forward to subscribed callback
            if (_progressCallbacks.TryGetValue(notification.JobId, out var callback))
            {
                callback(notification);
            }
        });

        _connection.On<JobCompletedNotification>("Completed", (notification) =>
        {
            _logger.LogInformation("Job {JobId} completed: Success={Success}",
                notification.JobId, notification.Success);

            // Signal TaskCompletionSource if someone is waiting for this job
            if (_jobCompletions.TryGetValue(notification.JobId, out var tcs))
            {
                tcs.TrySetResult(notification);
                _logger.LogDebug("Signaled TaskCompletionSource for job {JobId}", notification.JobId);
            }
        });

        _connection.Closed += async (error) =>
        {
            _logger.LogWarning(error, "SignalR connection closed");
            await Task.Delay(5000, cancellationToken); // Wait before reconnect
            try
            {
                await ConnectAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reconnect to SignalR hub");
            }
        };

        _connection.Reconnecting += (error) =>
        {
            _logger.LogWarning(error, "SignalR connection reconnecting...");
            return Task.CompletedTask;
        };

        _connection.Reconnected += (connectionId) =>
        {
            _logger.LogInformation("SignalR connection reconnected: {ConnectionId}", connectionId);
            return Task.CompletedTask;
        };

        try
        {
            await _connection.StartAsync(cancellationToken);
            _logger.LogInformation("Connected to SignalR hub successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to SignalR hub: {HubUrl}", _hubUrl);
            throw;
        }
    }

    public async Task SubscribeToJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        if (_connection == null || !IsConnected)
        {
            _logger.LogWarning("Cannot subscribe to job {JobId}: Not connected to SignalR hub", jobId);
            await ConnectAsync(cancellationToken);
        }

        // Create TaskCompletionSource for waiting on job completion
        var tcs = new TaskCompletionSource<JobCompletedNotification>(TaskCreationOptions.RunContinuationsAsynchronously);
        _jobCompletions[jobId] = tcs;
        _logger.LogDebug("Created TaskCompletionSource for job {JobId}", jobId);

        try
        {
            await _connection!.InvokeAsync("SubscribeToJob", jobId, cancellationToken);
            _logger.LogDebug("Subscribed to job {JobId}", jobId);
        }
        catch (Exception ex)
        {
            // Cleanup TCS on failure
            _jobCompletions.TryRemove(jobId, out _);
            _logger.LogError(ex, "Failed to subscribe to job {JobId}", jobId);
            throw;
        }
    }

    public async Task UnsubscribeFromJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        // Remove TaskCompletionSource from tracking (cleanup)
        if (_jobCompletions.TryRemove(jobId, out var tcs))
        {
            _logger.LogDebug("Removed TaskCompletionSource for job {JobId}", jobId);

            // If job completion was never signaled, cancel it
            tcs.TrySetCanceled();
        }

        if (_connection == null || !IsConnected)
        {
            _logger.LogWarning("Cannot unsubscribe from job {JobId}: Not connected to SignalR hub", jobId);
            return;
        }

        try
        {
            await _connection.InvokeAsync("UnsubscribeFromJob", jobId, cancellationToken);
            _logger.LogDebug("Unsubscribed from job {JobId}", jobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unsubscribe from job {JobId}", jobId);
        }
    }

    public async Task<JobCompletedNotification> WaitForJobCompletionAsync(string jobId, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        // Get TaskCompletionSource for this job
        if (!_jobCompletions.TryGetValue(jobId, out var tcs))
        {
            var error = $"Job {jobId} is not being tracked. Call SubscribeToJobAsync first.";
            _logger.LogError(error);
            throw new InvalidOperationException(error);
        }

        _logger.LogDebug("Waiting for job {JobId} completion via SignalR (timeout: {Timeout})", jobId, timeout);

        try
        {
            // Wait for SignalR completion notification with timeout
            var notification = await tcs.Task.WaitAsync(timeout, cancellationToken);
            _logger.LogInformation("Job {JobId} completed via SignalR notification", jobId);
            return notification;
        }
        catch (TimeoutException)
        {
            _logger.LogError("Timeout waiting for job {JobId} completion after {Timeout}", jobId, timeout);
            throw new TimeoutException($"Job {jobId} did not complete within {timeout.TotalSeconds} seconds");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Waiting for job {JobId} completion was cancelled", jobId);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection != null)
        {
            try
            {
                await _connection.StopAsync();
                await _connection.DisposeAsync();
                _logger.LogInformation("SignalR client disposed");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing SignalR client");
            }
        }

        GC.SuppressFinalize(this);
    }
}
