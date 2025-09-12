using System.Text.Json;
using McpProxy.Models;

namespace McpProxy.Services;

public interface ICommunicationService
{
    /// <summary>
    /// Starts the MCP server and handles JSON-RPC communication over stdin/stdout
    /// </summary>
    /// <param name="handleToolCall">Delegate to handle tool execution calls</param>
    /// <param name="healthCheck">Optional health check function for service availability</param>
    Task RunAsync(Func<string, string?, JsonElement, Task<McpToolResult>> handleToolCall, Func<Task<bool>>? healthCheck = null);
    
    /// <summary>
    /// Sends an MCP response message to the client
    /// </summary>
    /// <param name="response">The MCP response to send</param>
    Task SendResponseAsync(McpResponse response);
    
    /// <summary>
    /// Sends an MCP error response to the client
    /// </summary>
    /// <param name="requestId">The request ID to respond to</param>
    /// <param name="error">The error details</param>
    Task SendErrorResponseAsync(int requestId, McpError error);
    
    /// <summary>
    /// Sends a progress notification to the client for long-running operations
    /// </summary>
    /// <param name="progressToken">The progress token from the original request</param>
    /// <param name="progress">Progress value between 0.0 and 1.0</param>
    /// <param name="message">Optional progress message</param>
    Task SendProgressNotificationAsync(string progressToken, double progress, string? message = null);
}