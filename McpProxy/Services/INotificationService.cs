namespace McpProxy.Services;

public interface INotificationService
{
    void SetWriter(StreamWriter writer);
    Task SendInitializedNotificationAsync();
    Task SendProgressNotificationAsync(string progressToken, double progress, string? message = null);
}