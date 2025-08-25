using System.Text.Json;
using McpProxy.Models;
using Microsoft.Extensions.Logging;

namespace McpProxy.Services;

public class CommunicationService : ICommunicationService
{
    private readonly ILogger<CommunicationService> _logger;
    private readonly IToolsService _toolsService;
    private readonly INotificationService _notificationService;
    private StreamWriter? _writer;
    private bool _isInitialized = false;

    public CommunicationService(
        ILogger<CommunicationService> logger,
        IToolsService toolsService,
        INotificationService notificationService)
    {
        _logger = logger;
        _toolsService = toolsService;
        _notificationService = notificationService;
    }

    public async Task RunAsync(Func<string, string?, JsonElement, Task<McpToolResult>> handleToolCall, Func<Task<bool>>? healthCheck = null)
    {
        _logger.LogInformation("Starting CDB MCP Server Proxy...");

        // Read from stdin, respond to stdout
        var stdinStream = Console.OpenStandardInput();
        var stdoutStream = Console.OpenStandardOutput();

        using var reader = new StreamReader(stdinStream);
        using var writer = new StreamWriter(stdoutStream) { AutoFlush = true };
        _writer = writer;
        _notificationService.SetWriter(writer);

        _logger.LogInformation("MCP Server Proxy ready to accept requests");

        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            _logger.LogInformation("Received request: {Line}", line);

            try
            {
                var request = JsonSerializer.Deserialize<McpRequest>(line);
                if (request != null)
                {
                    _logger.LogInformation("Processing method: {Method}, ID: {Id}", request.Method, request.Id);
                    var response = await HandleMcpRequestAsync(request, handleToolCall, healthCheck);
                    if (response != null)
                    {
                        await SendResponseAsync(response);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing request: {Line}", line);
                var errorResponse = new McpResponse
                {
                    Id = 0,
                    Error = new McpError { Code = -1, Message = ex.Message }
                };
                await SendResponseAsync(errorResponse);
            }
        }

        _logger.LogInformation("MCP Server Proxy shutting down");
    }

    private async Task<McpResponse?> HandleMcpRequestAsync(McpRequest request, Func<string, string?, JsonElement, Task<McpToolResult>> handleToolCall, Func<Task<bool>>? healthCheck)
    {
        try
        {
            return request.Method switch
            {
                "initialize" => await HandleInitializeAsync(request.Id, healthCheck),
                "tools/list" => _isInitialized ? _toolsService.CreateListToolsResponse(request.Id) : CreateNotInitializedError(request.Id),
                "tools/call" => _isInitialized ? await HandleToolCallAsync(request, handleToolCall) : CreateNotInitializedError(request.Id),
                _ => CreateMethodNotFoundError(request.Id, request.Method)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling request method: {Method}", request.Method);
            return new McpResponse
            {
                Id = request.Id,
                Error = new McpError { Code = -1, Message = ex.Message }
            };
        }
    }

    private async Task<McpResponse> HandleInitializeAsync(int requestId, Func<Task<bool>>? healthCheck)
    {
        _logger.LogInformation("Received initialize request");

        // Check if background service is available
        var isHealthy = true;
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
        var response = CreateInitializeResponse(requestId, isHealthy);

        if (response.Error == null)
        {
            await _notificationService.SendInitializedNotificationAsync();
        }

        return response;
    }

    private static McpResponse CreateInitializeResponse(int requestId, bool isHealthy)
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

    private async Task<McpResponse> HandleToolCallAsync(McpRequest request, Func<string, string?, JsonElement, Task<McpToolResult>> handleToolCall)
    {
        if (request.Params == null)
        {
            return CreateInvalidParamsError(request.Id);
        }

        return await _toolsService.HandleToolCallAsync(request.Id, request.Params.Value, handleToolCall);
    }

    public async Task SendResponseAsync(McpResponse response)
    {
        if (_writer == null) return;

        var responseJson = JsonSerializer.Serialize(response);
        _logger.LogInformation("Sending response: {Response}", responseJson);
        await _writer.WriteLineAsync(responseJson);
    }

    public async Task SendErrorResponseAsync(int requestId, McpError error)
    {
        var errorResponse = new McpResponse
        {
            Id = requestId,
            Error = error
        };
        await SendResponseAsync(errorResponse);
    }

    private static McpResponse CreateNotInitializedError(int requestId)
    {
        return new McpResponse
        {
            Id = requestId,
            Error = new McpError { Code = -32002, Message = "Server not initialized" }
        };
    }

    private static McpResponse CreateMethodNotFoundError(int requestId, string method)
    {
        return new McpResponse
        {
            Id = requestId,
            Error = new McpError { Code = -32601, Message = $"Method not found: {method}" }
        };
    }

    private static McpResponse CreateInvalidParamsError(int requestId)
    {
        return new McpResponse
        {
            Id = requestId,
            Error = new McpError { Code = -32602, Message = "Invalid params" }
        };
    }
}