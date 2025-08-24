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
    
    // MCP Protocol handling
    McpResponse CreateInitializeResponse(int requestId, bool isHealthy);
    McpResponse CreateListToolsResponse(int requestId);
    McpResponse CreateToolCallResponse(int requestId, McpToolResult result);
    McpResponse CreateNotInitializedError(int requestId);
    McpResponse CreateMethodNotFoundError(int requestId, string method);
    McpResponse CreateInvalidParamsError(int requestId);
}