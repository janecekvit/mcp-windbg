using CdbMcpServer.Models;
using Microsoft.Extensions.Logging;

namespace McpProxy.Services;

public class McpInitializationService : IMcpInitializationService
{
    private readonly ILogger<McpInitializationService> _logger;
    private bool _isInitialized = false;

    public McpInitializationService(ILogger<McpInitializationService> logger)
    {
        _logger = logger;
    }

    public bool IsInitialized => _isInitialized;

    public async Task<McpResponse> HandleInitializeAsync(int requestId, Func<Task<bool>>? healthCheck = null)
    {
        _logger.LogInformation("Received initialize request");
        
        // Check if background service is available
        bool isHealthy = true;
        if (healthCheck != null)
        {
            isHealthy = await healthCheck();
            if (isHealthy)
            {
                _logger.LogInformation("Background service is healthy");
            }
            else
            {
                _logger.LogWarning("Background service health check failed");
            }
        }
        
        _isInitialized = true;
        return CreateInitializeResponse(requestId, isHealthy);
    }

    public McpResponse CreateInitializeResponse(int requestId, bool isHealthy)
    {
        return new McpResponse
        {
            Id = requestId,
            Result = new
            {
                protocolVersion = "2024-11-05",
                capabilities = new
                {
                    tools = new
                    {
                        listChanged = false
                    }
                },
                serverInfo = new
                {
                    name = "cdb-mcp-server-proxy",
                    version = "2.0.0"
                }
            }
        };
    }
}