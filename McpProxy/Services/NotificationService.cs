using System.Text.Json;

namespace McpProxy.Services;

public class NotificationService : INotificationService
{
    private StreamWriter? _writer;

    public void SetWriter(StreamWriter writer)
    {
        _writer = writer;
    }

    public async Task SendInitializedNotificationAsync()
    {
        if (_writer == null) return;

        var notification = new
        {
            jsonrpc = "2.0",
            method = "notifications/initialized"
        };

        var json = JsonSerializer.Serialize(notification);
        await _writer.WriteLineAsync(json);
    }

    public async Task SendProgressNotificationAsync(string progressToken, double progress, string? message = null)
    {
        if (_writer == null) return;

        var notification = new
        {
            jsonrpc = "2.0",
            method = "notifications/progress",
            @params = new
            {
                progressToken,
                progress,
                total = 1.0,
                message
            }
        };

        var json = JsonSerializer.Serialize(notification);
        await _writer.WriteLineAsync(json);
    }
}