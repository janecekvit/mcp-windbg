using System.Text.Json;
using CdbMcpServer.Models;

namespace McpProxy.Services;

public interface IMcpToolsService
{
    McpResponse CreateListToolsResponse(int requestId);
    Task<McpResponse> HandleToolCallAsync(int requestId, JsonElement args, Func<string, string?, JsonElement, Task<McpToolResult>> toolHandler);
    McpResponse CreateToolCallResponse(int requestId, McpToolResult result);
}