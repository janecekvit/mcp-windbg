using CdbMcpServer.Models;

namespace McpProxy.Services;

public interface IMcpInitializationService
{
    Task<McpResponse> HandleInitializeAsync(int requestId, Func<Task<bool>>? healthCheck = null);
    McpResponse CreateInitializeResponse(int requestId, bool isHealthy);
    bool IsInitialized { get; }
}