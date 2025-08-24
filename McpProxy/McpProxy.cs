using System.Text.Json;
using CdbMcpServer.Models;
using McpProxy.Services;
using Microsoft.Extensions.Logging;

namespace McpProxy;

public class McpProxy
{
    private readonly ILogger<McpProxy> _logger;
    private readonly IDebuggerApiService _debuggerApiService;
    private readonly ICommunicationService _communicationService;

    public McpProxy(ILogger<McpProxy> logger, IDebuggerApiService debuggerApiService, ICommunicationService communicationService)
    {
        _logger = logger;
        _debuggerApiService = debuggerApiService;
        _communicationService = communicationService;
    }

    public async Task RunAsync()
    {
        await _communicationService.RunAsync(HandleToolCallAsync, _debuggerApiService.CheckHealthAsync);
    }

    private async Task<McpToolResult> HandleToolCallAsync(string toolName, string? progressToken, JsonElement args)
    {
        _logger.LogInformation("Executing tool: {ToolName}", toolName);

        try
        {
            return toolName switch
            {
                "load_dump" => await _debuggerApiService.LoadDumpAsync(args, progressToken),
                "execute_command" => await _debuggerApiService.ExecuteCommandAsync(args, progressToken),
                "basic_analysis" => await _debuggerApiService.BasicAnalysisAsync(args, progressToken),
                "predefined_analysis" => await _debuggerApiService.PredefinedAnalysisAsync(args, progressToken),
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing tool: {ToolName}", toolName);
            return new McpToolResult
            {
                Content = new[] { new McpContent { Type = "text", Text = $"Error executing tool {toolName}: {ex.Message}" } },
                IsError = true
            };
        }
    }
}