using System.Text.Json;
using McpProxy.Models;
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

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await _communicationService.RunAsync(HandleToolCallAsync, _debuggerApiService.CheckHealthAsync, cancellationToken);
    }

    private async Task<McpToolResult> HandleToolCallAsync(string toolName, string? progressToken, JsonElement args, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing tool: {ToolName}", toolName);

        try
        {
            return toolName switch
            {
                "load_dump" => await _debuggerApiService.LoadDumpAsync(args, progressToken, cancellationToken),
                "execute_command" => await _debuggerApiService.ExecuteCommandAsync(args, progressToken, cancellationToken),
                "basic_analysis" => await _debuggerApiService.BasicAnalysisAsync(args, progressToken, cancellationToken),
                "predefined_analysis" => await _debuggerApiService.PredefinedAnalysisAsync(args, progressToken, cancellationToken),
                "list_sessions" => await _debuggerApiService.ListSessionsAsync(cancellationToken),
                "list_analyses" => await _debuggerApiService.ListAnalysesAsync(cancellationToken),
                "detect_debuggers" => await _debuggerApiService.DetectDebuggersAsync(cancellationToken),
                "close_session" => await _debuggerApiService.CloseSessionAsync(args, cancellationToken),
                _ => McpToolResult.Error($"Unknown tool: {toolName}")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing tool: {ToolName}", toolName);
            return McpToolResult.Error(ex, $"Error executing tool {toolName}");
        }
    }
}