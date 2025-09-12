using System.Text.Json;
using Common;
using McpProxy.Models;
using Microsoft.Extensions.Logging;

namespace McpProxy.Services;

public class DebuggerApiService : IDebuggerApiService
{
    private readonly ILogger<DebuggerApiService> _logger;
    private readonly IApiHttpClient _httpClient;
    private readonly INotificationService _notificationService;

    public DebuggerApiService(
        ILogger<DebuggerApiService> logger,
        IApiHttpClient httpClient,
        INotificationService notificationService)
    {
        _logger = logger;
        _httpClient = httpClient;
        _notificationService = notificationService;
    }

    public async Task<bool> CheckHealthAsync()
    {
        var result = await _httpClient.CheckHealthAsync();
        return result.IsSuccess && result.Value;
    }

    private async Task SendProgress(string? token, double progress, string message)
    {
        if (!string.IsNullOrEmpty(token))
            await _notificationService.SendProgressNotificationAsync(token, progress, message);
    }

    public async Task<McpToolResult> LoadDumpAsync(JsonElement args, string? progressToken = null)
    {
        if (!args.TryGetProperty("dump_file_path", out var pathElement))
            return McpToolResult.Error("Missing dump_file_path parameter");
            
        var dumpFilePath = pathElement.GetString();
        var error = ValidationHelper.ValidateDumpFilePath(dumpFilePath);
        if (error != null) return McpToolResult.Error(error);

        await SendProgress(progressToken, 0.1, "Loading dump file...");
        var request = new LoadDumpRequest(dumpFilePath!);
        
        var result = await _httpClient.PostAsync<LoadDumpRequest, LoadDumpResponse>(ApiEndpoints.LoadDump, request);
        if (result.IsFailure) return McpToolResult.Error(result.Error);

        await SendProgress(progressToken, 1.0, "Dump loaded successfully!");
        var response = result.Value;
        return McpToolResult.Success($"Session created: {response.SessionId}\nDump: {dumpFilePath}\n\n{response.Message}");
    }

    public async Task<McpToolResult> ExecuteCommandAsync(JsonElement args, string? progressToken = null)
    {
        if (!args.TryGetProperty("session_id", out var sessionElement) || 
            !args.TryGetProperty("command", out var cmdElement))
            return McpToolResult.Error("Missing session_id or command parameter");

        var sessionId = sessionElement.GetString();
        var command = cmdElement.GetString();
        
        var sessionError = ValidationHelper.ValidateSessionId(sessionId);
        if (sessionError != null) return McpToolResult.Error(sessionError);
        
        var cmdError = ValidationHelper.ValidateCommand(command);
        if (cmdError != null) return McpToolResult.Error(cmdError);

        var request = new ExecuteCommandRequest(sessionId!, command!);
        var result = await _httpClient.PostAsync<ExecuteCommandRequest, CommandExecutionResponse>(ApiEndpoints.ExecuteCommand, request);
        
        return result.IsSuccess ? McpToolResult.Success(result.Value.Result) : McpToolResult.Error(result.Error);
    }

    public async Task<McpToolResult> BasicAnalysisAsync(JsonElement args, string? progressToken = null)
    {
        if (!args.TryGetProperty("session_id", out var sessionElement))
            return McpToolResult.Error("Missing session_id parameter");

        var sessionId = sessionElement.GetString();
        var error = ValidationHelper.ValidateSessionId(sessionId);
        if (error != null) return McpToolResult.Error(error);

        await SendProgress(progressToken, 0.1, "Running analysis...");
        var request = new BasicAnalysisRequest(sessionId!);
        
        var result = await _httpClient.PostAsync<BasicAnalysisRequest, CommandExecutionResponse>(ApiEndpoints.BasicAnalysis, request);
        if (result.IsFailure) return McpToolResult.Error(result.Error);

        await SendProgress(progressToken, 1.0, "Analysis completed!");
        return McpToolResult.Success(result.Value.Result);
    }

    public async Task<McpToolResult> PredefinedAnalysisAsync(JsonElement args, string? progressToken = null)
    {
        if (!args.TryGetProperty("session_id", out var sessionElement) ||
            !args.TryGetProperty("analysis_type", out var typeElement))
            return McpToolResult.Error("Missing session_id or analysis_type parameter");

        var sessionId = sessionElement.GetString();
        var analysisType = typeElement.GetString();
        
        var sessionError = ValidationHelper.ValidateSessionId(sessionId);
        if (sessionError != null) return McpToolResult.Error(sessionError);
        
        if (string.IsNullOrWhiteSpace(analysisType))
            return McpToolResult.Error("Analysis type is required");

        var request = new PredefinedAnalysisRequest(sessionId!, analysisType!);
        var result = await _httpClient.PostAsync<PredefinedAnalysisRequest, CommandExecutionResponse>(ApiEndpoints.PredefinedAnalysis, request);
        
        return result.IsSuccess ? McpToolResult.Success(result.Value.Result) : McpToolResult.Error(result.Error);
    }

    public async Task<McpToolResult> ListSessionsAsync()
    {
        var result = await _httpClient.GetAsync<SessionsResponse>(ApiEndpoints.Sessions);
        if (result.IsFailure) return McpToolResult.Error(result.Error);

        var output = new System.Text.StringBuilder("Active sessions:\n");
        foreach (var session in result.Value.Sessions)
        {
            output.AppendLine($"  Session: {session.SessionId}");
            output.AppendLine($"  Dump: {session.DumpFile}");
            output.AppendLine($"  Active: {session.IsActive}\n");
        }
        return McpToolResult.Success(output.ToString());
    }

    public async Task<McpToolResult> CloseSessionAsync(JsonElement args)
    {
        if (!args.TryGetProperty("session_id", out var sessionElement))
            return McpToolResult.Error("Missing session_id parameter");

        var sessionId = sessionElement.GetString();
        var error = ValidationHelper.ValidateSessionId(sessionId);
        if (error != null) return McpToolResult.Error(error);

        var result = await _httpClient.DeleteAsync<CloseSessionResponse>(ApiEndpoints.SessionById(sessionId!));
        return result.IsSuccess ? McpToolResult.Success(result.Value.Message) : McpToolResult.Error(result.Error);
    }

    public async Task<McpToolResult> DetectDebuggersAsync()
    {
        var result = await _httpClient.GetAsync<DebuggerDetectionResponse>(ApiEndpoints.DetectDebuggers);
        if (result.IsFailure) return McpToolResult.Error(result.Error);

        var response = result.Value;
        var output = new System.Text.StringBuilder("🔍 Debugger Detection:\n\n");

        if (!string.IsNullOrEmpty(response.CdbPath))
            output.AppendLine($"✅ CDB: {response.CdbPath}");
        else
            output.AppendLine("❌ No CDB found");

        if (!string.IsNullOrEmpty(response.WinDbgPath) && response.WinDbgPath != response.CdbPath)
            output.AppendLine($"📊 WinDbg: {response.WinDbgPath}");

        output.AppendLine("\n🔧 Environment:");
        foreach (var env in response.EnvironmentVariables)
            output.AppendLine($"  {env.Key}: {env.Value ?? "(not set)"}");

        return McpToolResult.Success(output.ToString());
    }

    public async Task<McpToolResult> ListAnalysesAsync()
    {
        var result = await _httpClient.GetAsync<AnalysesResponse>(ApiEndpoints.Analyses);
        if (result.IsFailure) return McpToolResult.Error(result.Error);

        var output = new System.Text.StringBuilder("Available analyses:\n\n");
        foreach (var analysis in result.Value.Analyses)
            output.AppendLine($"{analysis.Name}: {analysis.Description}");

        return McpToolResult.Success(output.ToString());
    }
}