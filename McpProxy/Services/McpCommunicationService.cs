using System.Text.Json;
using CdbMcpServer.Models;
using Microsoft.Extensions.Logging;

namespace McpProxy.Services;

public class McpCommunicationService : IMcpCommunicationService
{
    private readonly ILogger<McpCommunicationService> _logger;
    private StreamWriter? _writer;
    private bool _isInitialized = false;

    public McpCommunicationService(ILogger<McpCommunicationService> logger)
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

    private async Task<McpResponse?> HandleMcpRequestAsync(McpRequest request, Func<string, string?, JsonElement, Task<McpToolResult>> handleToolCall, Func<Task<bool>>? healthCheck)
    {
        try
        {
            return request.Method switch
            {
                "initialize" => await HandleInitializeAsync(request, healthCheck),
                "tools/list" => _isInitialized ? CreateListToolsResponse(request.Id) : CreateNotInitializedError(request.Id),
                "tools/call" => _isInitialized ? await HandleCallToolAsync(request, handleToolCall) : CreateNotInitializedError(request.Id),
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

    private async Task<McpResponse> HandleInitializeAsync(McpRequest request, Func<Task<bool>>? healthCheck)
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
        await SendInitializedNotificationAsync();
        return CreateInitializeResponse(request.Id, isHealthy);
    }

    private async Task<McpResponse> HandleCallToolAsync(McpRequest request, Func<string, string?, JsonElement, Task<McpToolResult>> handleToolCall)
    {
        if (request.Params == null || !request.Params.Value.TryGetProperty("name", out var nameElement) ||
            !request.Params.Value.TryGetProperty("arguments", out var argsElement))
        {
            return CreateInvalidParamsError(request.Id);
        }

        var toolName = nameElement.GetString() ?? "";
        
        // Extract progress token if available
        string? progressToken = null;
        if (request.Params.Value.TryGetProperty("_meta", out var metaElement) &&
            metaElement.TryGetProperty("progressToken", out var tokenElement))
        {
            progressToken = tokenElement.GetString();
        }

        var result = await handleToolCall(toolName, progressToken, argsElement);
        return CreateToolCallResponse(request.Id, result);
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

    public McpResponse CreateListToolsResponse(int requestId)
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

        return new McpResponse
        {
            Id = requestId,
            Result = new { tools }
        };
    }

    public McpResponse CreateToolCallResponse(int requestId, McpToolResult result)
    {
        return new McpResponse
        {
            Id = requestId,
            Result = result
        };
    }

    public McpResponse CreateNotInitializedError(int requestId)
    {
        return new McpResponse
        {
            Id = requestId,
            Error = new McpError { Code = -32002, Message = "Server not initialized" }
        };
    }

    public McpResponse CreateMethodNotFoundError(int requestId, string method)
    {
        return new McpResponse
        {
            Id = requestId,
            Error = new McpError { Code = -32601, Message = $"Method not found: {method}" }
        };
    }

    public McpResponse CreateInvalidParamsError(int requestId)
    {
        return new McpResponse
        {
            Id = requestId,
            Error = new McpError { Code = -32602, Message = "Invalid params" }
        };
    }
}