using System.Text.Json;
using McpProxy.Models;

namespace McpProxy.Services;

public interface IDebuggerApiService
{
    Task<bool> CheckHealthAsync();
    Task<McpToolResult> LoadDumpAsync(JsonElement args, string? progressToken = null);
    Task<McpToolResult> ExecuteCommandAsync(JsonElement args, string? progressToken = null);
    Task<McpToolResult> BasicAnalysisAsync(JsonElement args, string? progressToken = null);
    Task<McpToolResult> PredefinedAnalysisAsync(JsonElement args, string? progressToken = null);
    Task<McpToolResult> ListSessionsAsync();
    Task<McpToolResult> CloseSessionAsync(JsonElement args);
    Task<McpToolResult> DetectDebuggersAsync();
    Task<McpToolResult> ListAnalysesAsync();
}