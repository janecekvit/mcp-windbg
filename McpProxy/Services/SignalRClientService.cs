using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Shared.Models;

namespace McpProxy.Services;

public class SignalRClientService : ISignalRClientService
{
    private readonly ILogger<SignalRClientService> _logger;
    private readonly ICommunicationService _communicationService;
    private readonly string _hubUrl;
    private HubConnection? _connection;

    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    public SignalRClientService(
        ILogger<SignalRClientService> logger,
        ICommunicationService communicationService,
        string hubUrl = "http://localhost:8080/hubs/progress")
    {
        _logger = logger;
        _communicationService = communicationService;
        _hubUrl = hubUrl;
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
        _connection.On<ProgressNotification>("Progress", async (notification) =>
        {
            _logger.LogDebug("Received progress notification for job {JobId}: {Progress:P0} - {Message}",
                notification.JobId, notification.Progress, notification.Message);

            // Forward progress to MCP client (Claude)
            await _communicationService.SendProgressNotificationAsync(
                notification.JobId,
                notification.Progress,
                notification.Message,
                cancellationToken);
        });

        _connection.On<JobCompletedNotification>("Completed", (notification) =>
        {
            _logger.LogInformation("Job {JobId} completed: Success={Success}",
                notification.JobId, notification.Success);
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

        try
        {
            await _connection!.InvokeAsync("SubscribeToJob", jobId, cancellationToken);
            _logger.LogDebug("Subscribed to job {JobId}", jobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to subscribe to job {JobId}", jobId);
            throw;
        }
    }

    public async Task UnsubscribeFromJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
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
