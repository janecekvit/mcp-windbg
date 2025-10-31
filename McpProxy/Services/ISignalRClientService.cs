namespace McpProxy.Services;

public interface ISignalRClientService : IAsyncDisposable
{
    /// <summary>
    /// Connects to the SignalR hub
    /// </summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to progress updates for a specific job
    /// </summary>
    Task SubscribeToJobAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unsubscribes from progress updates for a specific job
    /// </summary>
    Task UnsubscribeFromJobAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Waits for a job to complete via SignalR completion notification
    /// </summary>
    /// <param name="jobId">The job ID to wait for</param>
    /// <param name="timeout">Maximum time to wait for completion</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The job completion notification</returns>
    /// <exception cref="TimeoutException">Thrown if job doesn't complete within timeout</exception>
    Task<Shared.Models.JobCompletedNotification> WaitForJobCompletionAsync(string jobId, TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true if connected to hub
    /// </summary>
    bool IsConnected { get; }
}
