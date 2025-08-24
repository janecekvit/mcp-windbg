using System.Text.Json;
using CdbMcpServer.Models;
using McpProxy.Services;
using Microsoft.Extensions.Logging;

namespace McpProxy;

public class McpProxy
{
    private readonly ILogger<McpProxy> _logger;
    private readonly IDebuggerApiService _debuggerApiService;
    private bool _isInitialized = false;

    public McpProxy(ILogger<McpProxy> logger, IDebuggerApiService debuggerApiService)
    {
        _logger = logger;
        _debuggerApiService = debuggerApiService;
    }

    public async Task RunAsync()
    {
        _logger.LogInformation("Starting CDB MCP Server Proxy...");

        // Read from stdin, respond to stdout
        var stdinStream = Console.OpenStandardInput();
        var stdoutStream = Console.OpenStandardOutput();

        using var reader = new StreamReader(stdinStream);
        using var writer = new StreamWriter(stdoutStream) { AutoFlush = true };

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
                    var response = await HandleRequestAsync(request, writer);
                    if (response != null)
                    {
                        var responseJson = JsonSerializer.Serialize(response);
                        _logger.LogInformation("Sending response: {Response}", responseJson);
                        await writer.WriteLineAsync(responseJson);
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
                var errorJson = JsonSerializer.Serialize(errorResponse);
                await writer.WriteLineAsync(errorJson);
            }
        }

        _logger.LogInformation("MCP Server Proxy shutting down");
    }

    private static async Task SendInitializedNotification(StreamWriter writer)
    {
        var notification = new
        {
            jsonrpc = "2.0",
            method = "notifications/initialized"
        };

        var json = JsonSerializer.Serialize(notification);
        await writer.WriteLineAsync(json);
    }

    private static async Task SendProgressNotification(StreamWriter writer, string progressToken, double progress, string? message = null)
    {
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
        await writer.WriteLineAsync(json);
    }

    private async Task<McpResponse?> HandleRequestAsync(McpRequest request, StreamWriter writer)
    {
        try
        {
            return request.Method switch
            {
                "initialize" => await HandleInitializeAsync(request, writer),
                "tools/list" => _isInitialized ? HandleListToolsAsync(request) : CreateNotInitializedError(request),
                "tools/call" => _isInitialized ? await HandleCallToolAsync(request, writer) : CreateNotInitializedError(request),
                _ => new McpResponse
                {
                    Id = request.Id,
                    Error = new McpError { Code = -32601, Message = $"Method not found: {request.Method}" }
                }
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

    private async Task<McpResponse?> HandleInitializeAsync(McpRequest request, StreamWriter writer)
    {
        _logger.LogInformation("Received initialize request");

        // Check if background service is available
        var isHealthy = await _debuggerApiService.CheckHealthAsync();
        if (isHealthy)
        {
            _logger.LogInformation("Background service is healthy");
        }
        else
        {
            _logger.LogWarning("Background service health check failed");
        }

        var initializeResponse = new McpResponse
        {
            Id = request.Id,
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

        _isInitialized = true;

        // Send notification after initialization
        await SendInitializedNotification(writer);

        // Return response normally
        return initializeResponse;
    }

    private static McpResponse CreateNotInitializedError(McpRequest request)
    {
        return new McpResponse
        {
            Id = request.Id,
            Error = new McpError { Code = -32002, Message = "Server not initialized" }
        };
    }

    private static McpResponse HandleListToolsAsync(McpRequest request)
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
            Id = request.Id,
            Result = new { tools }
        };
    }

    private async Task<McpResponse> HandleCallToolAsync(McpRequest request, StreamWriter writer)
    {
        if (request.Params == null || !request.Params.Value.TryGetProperty("name", out var nameElement) ||
            !request.Params.Value.TryGetProperty("arguments", out var argsElement))
        {
            return new McpResponse
            {
                Id = request.Id,
                Error = new McpError { Code = -32602, Message = "Invalid params" }
            };
        }

        var toolName = nameElement.GetString() ?? "";
        var args = argsElement;

        // Extract progress token if available
        string? progressToken = null;
        if (request.Params.Value.TryGetProperty("_meta", out var metaElement) &&
            metaElement.TryGetProperty("progressToken", out var tokenElement))
        {
            progressToken = tokenElement.GetString();
        }

        var result = toolName switch
        {
            "load_dump" => await _debuggerApiService.LoadDumpAsync(args, progressToken, writer),
            "execute_command" => await _debuggerApiService.ExecuteCommandAsync(args, progressToken, writer),
            "basic_analysis" => await _debuggerApiService.BasicAnalysisAsync(args, progressToken, writer),
            "predefined_analysis" => await _debuggerApiService.PredefinedAnalysisAsync(args, progressToken, writer),
            "list_sessions" => await _debuggerApiService.ListSessionsAsync(),
            "list_analyses" => await _debuggerApiService.ListAnalysesAsync(),
            "detect_debuggers" => await _debuggerApiService.DetectDebuggersAsync(),
            "close_session" => await _debuggerApiService.CloseSessionAsync(args),
            _ => new McpToolResult
            {
                Content = new[] { new McpContent { Type = "text", Text = $"Unknown tool: {toolName}" } },
                IsError = true
            }
        };

        return new McpResponse
        {
            Id = request.Id,
            Result = result
        };
    }

}