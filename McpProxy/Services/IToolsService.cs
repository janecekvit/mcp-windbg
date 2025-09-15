using McpProxy.Models;

namespace McpProxy.Services;

public interface IToolsService
{
    /// <summary>
    /// Creates an MCP response containing the list of available debugging tools
    /// </summary>
    /// <param name="requestId">The request ID to include in the response</param>
    /// <returns>MCP response with tool definitions including schemas and descriptions</returns>
    McpResponse CreateListToolsResponse(int requestId);
}