using System.Text.Json;
using McpProxy.Models;
using Microsoft.Extensions.Logging;

namespace McpProxy.Services;

public class CommunicationService : ICommunicationService
{
    private readonly ILogger<CommunicationService> _logger;
    private StreamWriter? _writer;
    private bool _isInitialized = false;

    public CommunicationService(
        ILogger<CommunicationService> logger)
    {
        _logger = logger;
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
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing request: {Line}", line);
                var errorResponse = McpResponse.CreateError(0, McpError.Custom(-1, ex.Message));
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
                "tools/list" => _isInitialized ? CreateListToolsResponse(request.Id) : CreateNotInitializedError(request.Id),
                "tools/call" => _isInitialized ? await HandleToolCallAsync(request, handleToolCall) : CreateNotInitializedError(request.Id),
                _ => CreateMethodNotFoundError(request.Id, request.Method)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling request method: {Method}", request.Method);
            return McpResponse.CreateError(request.Id, -1, ex.Message);
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
            await SendInitializedNotificationAsync();
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

    private async Task<McpResponse> HandleToolCallAsync(McpRequest request, Func<string, string?, JsonElement, Task<McpToolResult>> handleToolCall)
    {
        if (request.Params == null)
        {
            return CreateInvalidParamsError(request.Id);
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

        var result = await handleToolCall(toolName, progressToken, argsElement);
        return McpResponse.Success(request.Id, result);
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
        var errorResponse = McpResponse.CreateError(requestId, error);
        await SendResponseAsync(errorResponse);
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

    private async Task SendInitializedNotificationAsync()
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

    private static McpResponse CreateNotInitializedError(int requestId)
    {
        return McpResponse.NotInitialized(requestId);
    }

    private static McpResponse CreateMethodNotFoundError(int requestId, string method)
    {
        return McpResponse.MethodNotFound(requestId, method);
    }

    private static McpResponse CreateInvalidParamsError(int requestId)
    {
        return McpResponse.InvalidParams(requestId);
    }

    private McpResponse CreateListToolsResponse(int requestId)
    {
        var tools = new[]
        {
            new McpTool
            {
                Name = "load_dump",
                Description = "Load a memory dump file and create a new CDB debugging session",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        dump_file_path = new
                        {
                            type = "string",
                            description = "Path to the memory dump file (.dmp)"
                        }
                    },
                    required = new[] { "dump_file_path" }
                }
            },
            new McpTool
            {
                Name = "execute_command",
                Description = "Execute a WinDbg/CDB command in an existing debugging session",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        session_id = new
                        {
                            type = "string",
                            description = "ID of the debugging session"
                        },
                        command = new
                        {
                            type = "string",
                            description = "WinDbg/CDB command to execute (e.g., 'kb', '!analyze -v', 'dt')"
                        }
                    },
                    required = new[] { "session_id", "command" }
                }
            },
            new McpTool
            {
                Name = "basic_analysis",
                Description = "Run a comprehensive basic analysis of the loaded dump (equivalent to the PowerShell script)",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        session_id = new
                        {
                            type = "string",
                            description = "ID of the debugging session"
                        }
                    },
                    required = new[] { "session_id" }
                }
            },
            new McpTool
            {
                Name = "list_sessions",
                Description = "List all active debugging sessions",
                InputSchema = new
                {
                    type = "object",
                    properties = new { }
                }
            },
            new McpTool
            {
                Name = "close_session",
                Description = "Close a debugging session and free resources",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        session_id = new
                        {
                            type = "string",
                            description = "ID of the debugging session to close"
                        }
                    },
                    required = new[] { "session_id" }
                }
            },
            new McpTool
            {
                Name = "predefined_analysis",
                Description = "Run a predefined analysis on the loaded dump (basic, exception, threads, heap, modules, handles, locks, memory, drivers, processes)",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        session_id = new
                        {
                            type = "string",
                            description = "ID of the debugging session"
                        },
                        analysis_type = new
                        {
                            type = "string",
                            description = "Type of analysis to run",
                            @enum = new[] { "basic", "exception", "threads", "heap", "modules", "handles", "locks", "memory", "drivers", "processes" }
                        }
                    },
                    required = new[] { "session_id", "analysis_type" }
                }
            },
            new McpTool
            {
                Name = "list_analyses",
                Description = "List all available predefined analyses with descriptions",
                InputSchema = new
                {
                    type = "object",
                    properties = new { }
                }
            },
            new McpTool
            {
                Name = "detect_debuggers",
                Description = "Detect available CDB/WinDbg installations on the system",
                InputSchema = new
                {
                    type = "object",
                    properties = new { }
                }
            }
        };

        return McpResponse.Success(requestId, new { tools });
    }
}