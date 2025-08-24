using System.Text.Json;
using CdbMcpServer.Models;
using Microsoft.Extensions.Logging;

namespace McpProxy.Services;

public class McpCommunicationService : IMcpCommunicationService
{
    private readonly ILogger<McpCommunicationService> _logger;
    private readonly IMcpInitializationService _initializationService;
    private readonly IMcpToolsService _toolsService;
    private StreamWriter? _writer;

    public McpCommunicationService(
        ILogger<McpCommunicationService> logger,
        IMcpInitializationService initializationService,
        IMcpToolsService toolsService)
    {
        _logger = logger;
        _initializationService = initializationService;
        _toolsService = toolsService;
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
                        
                        // Send initialized notification after successful initialize
                        if (request.Method == "initialize" && response.Error == null)
                        {
                            await SendInitializedNotificationAsync();
                        }
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
                "initialize" => await _initializationService.HandleInitializeAsync(request.Id, healthCheck),
                "tools/list" => _initializationService.IsInitialized ? _toolsService.CreateListToolsResponse(request.Id) : CreateNotInitializedError(request.Id),
                "tools/call" => _initializationService.IsInitialized ? await HandleToolCallAsync(request, handleToolCall) : CreateNotInitializedError(request.Id),
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

    private async Task<McpResponse> HandleToolCallAsync(McpRequest request, Func<string, string?, JsonElement, Task<McpToolResult>> handleToolCall)
    {
        if (request.Params == null)
        {
            return CreateInvalidParamsError(request.Id);
        }

        return await _toolsService.HandleToolCallAsync(request.Id, request.Params.Value, handleToolCall);
    }

    public async Task SendInitializedNotificationAsync()
    {
        if (_writer == null) return;

        var notification = new
        {
            jsonrpc = "2.0",
            method = "notifications/initialized"
        };

        var json = JsonSerializer.Serialize(notification);
        await _writer.WriteLineAsync(json);
    }

    public async Task SendProgressNotificationAsync(string progressToken, double progress, string? message = null)
    {
        if (_writer == null) return;

        var notification = new
        {
            jsonrpc = "2.0",
            method = "notifications/progress",
            @params = new
            {
                progressToken,
                progress,
                total = 1.0,
                message
            }
        };

        var json = JsonSerializer.Serialize(notification);
        await _writer.WriteLineAsync(json);
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

    private McpResponse CreateNotInitializedError(int requestId)
    {
        return new McpResponse
        {
            Id = requestId,
            Error = new McpError { Code = -32002, Message = "Server not initialized" }
        };
    }

    private McpResponse CreateMethodNotFoundError(int requestId, string method)
    {
        return new McpResponse
        {
            Id = requestId,
            Error = new McpError { Code = -32601, Message = $"Method not found: {method}" }
        };
    }

    private McpResponse CreateInvalidParamsError(int requestId)
    {
        return new McpResponse
        {
            Id = requestId,
            Error = new McpError { Code = -32602, Message = "Invalid params" }
        };
    }
}