using Common;
using McpProxy.Models;

namespace McpProxy.Services;

public class ToolsService : IToolsService
{
    public McpResponse CreateListToolsResponse(int requestId)
    {
        var tools = new[]
        {
            new McpTool
            {
                Name = McpToolNames.LoadDump,
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
                Name = McpToolNames.ExecuteCommand,
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
                Name = McpToolNames.BasicAnalysis,
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
                Name = McpToolNames.ListSessions,
                Description = "List all active debugging sessions",
                InputSchema = new
                {
                    type = "object",
                    properties = new { }
                }
            },
            new McpTool
            {
                Name = McpToolNames.CloseSession,
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
                Name = McpToolNames.PredefinedAnalysis,
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
                            @enum = AnalysisTypeExtensions.GetAllIdentifiers()
                        }
                    },
                    required = new[] { "session_id", "analysis_type" }
                }
            },
            new McpTool
            {
                Name = McpToolNames.ListAnalyses,
                Description = "List all available predefined analyses with descriptions",
                InputSchema = new
                {
                    type = "object",
                    properties = new { }
                }
            },
            new McpTool
            {
                Name = McpToolNames.DetectDebuggers,
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