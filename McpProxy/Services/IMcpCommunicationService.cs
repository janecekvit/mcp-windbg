using System.Text.Json;
using CdbMcpServer.Models;

namespace McpProxy.Services;

public interface IMcpCommunicationService
{
    Task RunAsync(Func<string, string?, JsonElement, Task<McpToolResult>> handleToolCall, Func<Task<bool>>? healthCheck = null);
    Task SendInitializedNotificationAsync();
    Task SendProgressNotificationAsync(string progressToken, double progress, string? message = null);
    Task SendResponseAsync(McpResponse response);
    Task SendErrorResponseAsync(int requestId, McpError error);
}