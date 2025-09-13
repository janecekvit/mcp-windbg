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
    /// <param name="cancellationToken">Token to cancel the operation</param>
    Task RunAsync(Func<string, string?, JsonElement, CancellationToken, Task<McpToolResult>> handleToolCall, Func<CancellationToken, Task<bool>>? healthCheck = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Sends an MCP response message to the client
    /// </summary>
    /// <param name="response">The MCP response to send</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    Task SendResponseAsync(McpResponse response, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Sends an MCP error response to the client
    /// </summary>
    /// <param name="requestId">The request ID to respond to</param>
    /// <param name="error">The error details</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    Task SendErrorResponseAsync(int requestId, McpError error, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Sends a progress notification to the client for long-running operations
    /// </summary>
    /// <param name="progressToken">The progress token from the original request</param>
    /// <param name="progress">Progress value between 0.0 and 1.0</param>
    /// <param name="message">Optional progress message</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    Task SendProgressNotificationAsync(string progressToken, double progress, string? message = null, CancellationToken cancellationToken = default);
}