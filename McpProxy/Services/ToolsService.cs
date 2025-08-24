using System.Text.Json;
using CdbMcpServer.Models;
using Microsoft.Extensions.Logging;

namespace McpProxy.Services;

public class ToolsService : IToolsService
{
    private readonly ILogger<ToolsService> _logger;

    public ToolsService(ILogger<ToolsService> logger)
    {
        _logger = logger;
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

    public async Task<McpResponse> HandleToolCallAsync(int requestId, JsonElement args, Func<string, string?, JsonElement, Task<McpToolResult>> toolHandler)
    {
        if (!args.TryGetProperty("name", out var nameElement) ||
            !args.TryGetProperty("arguments", out var argsElement))
        {
            return new McpResponse
            {
                Id = requestId,
                Error = new McpError { Code = -32602, Message = "Invalid params - missing name or arguments" }
            };
        }

        var toolName = nameElement.GetString() ?? "";
        
        // Extract progress token if available
        string? progressToken = null;
        if (args.TryGetProperty("_meta", out var metaElement) &&
            metaElement.TryGetProperty("progressToken", out var tokenElement))
        {
            progressToken = tokenElement.GetString();
        }

        var result = await toolHandler(toolName, progressToken, argsElement);
        return CreateToolCallResponse(requestId, result);
    }

    public McpResponse CreateToolCallResponse(int requestId, McpToolResult result)
    {
        return new McpResponse
        {
            Id = requestId,
            Result = result
        };
    }
}