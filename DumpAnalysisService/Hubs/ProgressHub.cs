using DumpAnalysisService.Security;
using DumpAnalysisService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace DumpAnalysisService.Hubs;

/// <summary>
/// SignalR Hub for real-time progress notifications
/// </summary>
[Authorize(Policy = DumpPolicies.Read)]
public class ProgressHub : Hub
{
    private readonly ILogger<ProgressHub> _logger;
    private readonly IJobManagerService _jobs;
    private readonly SecurityOptions _security;

    public ProgressHub(ILogger<ProgressHub> logger, IJobManagerService jobs, IOptions<SecurityOptions> security)
    {
        _logger = logger;
        _jobs = jobs;
        _security = security.Value;
    }

    /// <summary>
    /// Subscribe to progress updates for a specific job
    /// </summary>
    /// <exception cref="HubException">The job does not exist or belongs to another caller.</exception>
    public async Task SubscribeToJob(string jobId)
    {
        // Progress messages name dumps and commands: only the job's owner may follow them.
        if (!_jobs.CanAccessJob(Caller.From(Context.User, _security), jobId))
            throw new HubException($"Job {jobId} not found");

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
