using McpProxy.Models;

namespace McpProxy.Services;

public interface IToolsService
{
    McpResponse CreateListToolsResponse(int requestId);
}