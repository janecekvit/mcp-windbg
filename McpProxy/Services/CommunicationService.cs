using System.Text.Json;
using McpProxy.Models;
using Microsoft.Extensions.Logging;

namespace McpProxy.Services;

public class CommunicationService : ICommunicationService
{
    private readonly ILogger<CommunicationService> _logger;
    private readonly IToolsService _toolsService;
    private StreamWriter? _writer;
    private bool _isInitialized = false;

    public CommunicationService(
        ILogger<CommunicationService> logger,
        IToolsService toolsService)
    {
        _logger = logger;
        _toolsService = toolsService;
    }

    public async Task RunAsync(Func<string, string?, JsonElement, CancellationToken, Task<McpToolResult>> handleToolCall, Func<CancellationToken, Task<bool>>? healthCheck = null, CancellationToken cancellationToken = default)
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
        while ((line = await reader.ReadLineAsync().WaitAsync(cancellationToken)) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            _logger.LogInformation("Received request: {Line}", line);

            try
            {
                var request = JsonSerializer.Deserialize<McpRequest>(line);
                if (request != null)
                {
                    _logger.LogInformation("Processing method: {Method}, ID: {Id}", request.Method, request.Id);
                    var response = await HandleMcpRequestAsync(request, handleToolCall, healthCheck, cancellationToken);
                    if (response != null)
                    {
                        await SendResponseAsync(response);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing request: {Line}", line);
                var errorResponse = McpResponse.CreateError(0, McpError.Custom(-1, ex.Message));
                await SendResponseAsync(errorResponse, cancellationToken);
            }
        }

        _logger.LogInformation("MCP Server Proxy shutting down");
    }

    private async Task<McpResponse?> HandleMcpRequestAsync(McpRequest request, Func<string, string?, JsonElement, CancellationToken, Task<McpToolResult>> handleToolCall, Func<CancellationToken, Task<bool>>? healthCheck, CancellationToken cancellationToken = default)
    {
        try
        {
            return request.Method switch
            {
                "initialize" => await HandleInitializeAsync(request.Id, healthCheck, cancellationToken),
                "tools/list" => _isInitialized ? _toolsService.CreateListToolsResponse(request.Id) : McpResponse.NotInitialized(request.Id),
                "tools/call" => _isInitialized ? await HandleToolCallAsync(request, handleToolCall, cancellationToken) : McpResponse.NotInitialized(request.Id),
                _ => McpResponse.MethodNotFound(request.Id, request.Method)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling request method: {Method}", request.Method);
            return McpResponse.CreateError(request.Id, -1, ex.Message);
        }
    }

    private async Task<McpResponse> HandleInitializeAsync(int requestId, Func<CancellationToken, Task<bool>>? healthCheck, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Received initialize request");

        // Check if background service is available
        var isHealthy = true;
        if (healthCheck != null)
        {
            isHealthy = await healthCheck(cancellationToken);
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
            await SendInitializedNotificationAsync(cancellationToken);
        }

        return response;
    }

    private static McpResponse CreateInitializeResponse(int requestId, bool isHealthy)
    {
        return McpResponse.Success(requestId, new
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
        });
    }

    private static async Task<McpResponse> HandleToolCallAsync(McpRequest request, Func<string, string?, JsonElement, CancellationToken, Task<McpToolResult>> handleToolCall, CancellationToken cancellationToken = default)
    {
        if (request.Params == null)
        {
            return McpResponse.InvalidParams(request.Id);
        }

        var args = request.Params.Value;
        if (!args.TryGetProperty("name", out var nameElement) ||
            !args.TryGetProperty("arguments", out var argsElement))
        {
            return McpResponse.CreateError(request.Id, -32602, "Invalid params - missing name or arguments");
        }

        var toolName = nameElement.GetString() ?? "";

        // Extract progress token if available
        string? progressToken = null;
        if (args.TryGetProperty("_meta", out var metaElement) &&
            metaElement.TryGetProperty("progressToken", out var tokenElement))
        {
            progressToken = tokenElement.GetString();
        }

        var result = await handleToolCall(toolName, progressToken, argsElement, cancellationToken);
        return McpResponse.Success(request.Id, result);
    }

    public async Task SendResponseAsync(McpResponse response, CancellationToken cancellationToken = default)
    {
        if (_writer == null) return;

        var responseJson = JsonSerializer.Serialize(response);
        _logger.LogInformation("Sending response: {Response}", responseJson);
        await _writer.WriteLineAsync(responseJson).WaitAsync(cancellationToken);
    }

    public async Task SendErrorResponseAsync(int requestId, McpError error, CancellationToken cancellationToken = default)
    {
        var errorResponse = McpResponse.CreateError(requestId, error);
        await SendResponseAsync(errorResponse, cancellationToken);
    }

    public async Task SendProgressNotificationAsync(string progressToken, double progress, string? message = null, CancellationToken cancellationToken = default)
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
        await _writer.WriteLineAsync(json).WaitAsync(cancellationToken);
    }

    private async Task SendInitializedNotificationAsync(CancellationToken cancellationToken = default)
    {
        if (_writer == null) return;

        var notification = new
        {
            jsonrpc = "2.0",
            method = "notifications/initialized"
        };

        var json = JsonSerializer.Serialize(notification);
        await _writer.WriteLineAsync(json).WaitAsync(cancellationToken);
    }

}