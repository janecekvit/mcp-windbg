using Microsoft.AspNetCore.SignalR;

namespace DumpAnalysisService.Hubs;

/// <summary>
/// SignalR Hub for real-time progress notifications
/// </summary>
public class ProgressHub : Hub
{
    private readonly ILogger<ProgressHub> _logger;

    public ProgressHub(ILogger<ProgressHub> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Subscribe to progress updates for a specific job
    /// </summary>
    public async Task SubscribeToJob(string jobId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, jobId);
        _logger.LogInformation("Client {ConnectionId} subscribed to job {JobId}",
            Context.ConnectionId, jobId);
    }

    /// <summary>
    /// Unsubscribe from progress updates for a specific job
    /// </summary>
    public async Task UnsubscribeFromJob(string jobId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, jobId);
        _logger.LogInformation("Client {ConnectionId} unsubscribed from job {JobId}",
            Context.ConnectionId, jobId);
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
