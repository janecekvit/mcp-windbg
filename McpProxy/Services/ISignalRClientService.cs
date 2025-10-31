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
    /// Returns true if connected to hub
    /// </summary>
    bool IsConnected { get; }
}
