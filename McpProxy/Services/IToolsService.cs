using System.Text.Json;
using McpProxy.Models;

namespace McpProxy.Services;

public interface IToolsService
{
    McpResponse CreateListToolsResponse(int requestId);
    Task<McpResponse> HandleToolCallAsync(int requestId, JsonElement args, Func<string, string?, JsonElement, Task<McpToolResult>> toolHandler);
    McpResponse CreateToolCallResponse(int requestId, McpToolResult result);
}