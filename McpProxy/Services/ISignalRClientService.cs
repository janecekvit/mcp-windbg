using Shared.Models;

namespace McpProxy.Services;

public interface ISignalRClientService : IAsyncDisposable
{
    /// <summary>
    /// Connects to the SignalR hub
    /// </summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to progress updates for a specific job with callback
    /// </summary>
    /// <param name="jobId">The job ID to subscribe to</param>
    /// <param name="progressCallback">Callback invoked when progress notification is received</param>
    void SubscribeToJobProgress(string jobId, Action<ProgressNotification> progressCallback);

    /// <summary>
    /// Unsubscribes from progress updates for a specific job
    /// </summary>
    /// <param name="jobId">The job ID to unsubscribe from</param>
    void UnsubscribeFromJobProgress(string jobId);

    /// <summary>
    /// Subscribes to progress updates for a specific job (async version)
    /// </summary>
    Task SubscribeToJobAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unsubscribes from progress updates for a specific job (async version)
    /// </summary>
    Task UnsubscribeFromJobAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Waits for a job to complete via SignalR completion notification
    /// </summary>
    Task<JobCompletedNotification> WaitForJobCompletionAsync(string jobId, TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true if connected to hub
    /// </summary>
    bool IsConnected { get; }
}
