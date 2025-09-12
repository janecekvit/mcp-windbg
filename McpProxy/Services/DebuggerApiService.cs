using System.Text.Json;
using McpProxy.Constants;
using McpProxy.Models;
using Microsoft.Extensions.Logging;

namespace McpProxy.Services;

public class DebuggerApiService : IDebuggerApiService
{
    private readonly ILogger<DebuggerApiService> _logger;
    private readonly IApiHttpClient _httpClient;
    private readonly IValidationService _validationService;
    private readonly INotificationService _notificationService;

    public DebuggerApiService(
        ILogger<DebuggerApiService> logger,
        IApiHttpClient httpClient,
        IValidationService validationService,
        INotificationService notificationService)
    {
        _logger = logger;
        _httpClient = httpClient;
        _validationService = validationService;
        _notificationService = notificationService;
    }

    public async Task<bool> CheckHealthAsync()
    {
        var result = await _httpClient.CheckHealthAsync();
        return result.IsSuccess && result.Value;
    }

    private async Task SendProgressNotification(string? progressToken, double progress, string message)
    {
        if (!string.IsNullOrEmpty(progressToken))
            await _notificationService.SendProgressNotificationAsync(progressToken, progress, message);
    }

    public async Task<McpToolResult> LoadDumpAsync(JsonElement args, string? progressToken = null)
    {
        var validationResult = _validationService.ValidateDumpFilePath(args);
        if (validationResult.IsFailure)
            return McpToolResult.Error(validationResult.Error);

        await SendProgressNotification(progressToken, ProgressValues.ValidationStart, "Validating dump file path...");

        var request = new LoadDumpRequest(validationResult.Value);
        await SendProgressNotification(progressToken, ProgressValues.ProcessingStart, "Loading dump file...");

        var result = await _httpClient.PostAsync<LoadDumpRequest, LoadDumpResponse>(
            ApiEndpoints.LoadDump, request);

        if (result.IsFailure)
            return McpToolResult.Error(result.Error);

        await SendProgressNotification(progressToken, ProgressValues.ProcessingMiddle, "Creating debugging session...");
        var response = result.Value;
        await SendProgressNotification(progressToken, ProgressValues.Completed, "Dump loaded successfully!");

        return McpToolResult.Success(
            $"Session created successfully!\nSession ID: {response.SessionId}\nDump file: {validationResult.Value}\n\n{response.Message}");
    }

    public async Task<McpToolResult> ExecuteCommandAsync(JsonElement args, string? progressToken = null)
    {
        var validationResult = _validationService.ValidateExecuteCommand(args);
        if (validationResult.IsFailure)
            return McpToolResult.Error(validationResult.Error);

        var (sessionId, command) = validationResult.Value;
        var request = new ExecuteCommandRequest(sessionId, command);
        
        var result = await _httpClient.PostAsync<ExecuteCommandRequest, CommandExecutionResponse>(
            ApiEndpoints.ExecuteCommand, request);

        return result.IsSuccess 
            ? McpToolResult.Success(result.Value.Result) 
            : McpToolResult.Error(result.Error);
    }

    public async Task<McpToolResult> BasicAnalysisAsync(JsonElement args, string? progressToken = null)
    {
        var validationResult = _validationService.ValidateSessionId(args);
        if (validationResult.IsFailure)
            return McpToolResult.Error(validationResult.Error);

        await SendProgressNotification(progressToken, ProgressValues.ValidationStart, "Preparing basic analysis...");

        var request = new BasicAnalysisRequest(validationResult.Value);
        await SendProgressNotification(progressToken, ProgressValues.ProcessingStart, "Running comprehensive analysis...");

        var result = await _httpClient.PostAsync<BasicAnalysisRequest, CommandExecutionResponse>(
            ApiEndpoints.BasicAnalysis, request);

        if (result.IsFailure)
            return McpToolResult.Error(result.Error);

        await SendProgressNotification(progressToken, ProgressValues.ProcessingEnd, "Processing analysis results...");
        await SendProgressNotification(progressToken, ProgressValues.Completed, "Analysis completed!");

        return McpToolResult.Success(result.Value.Result);
    }

    public async Task<McpToolResult> PredefinedAnalysisAsync(JsonElement args, string? progressToken = null)
    {
        var validationResult = _validationService.ValidatePredefinedAnalysis(args);
        if (validationResult.IsFailure)
            return McpToolResult.Error(validationResult.Error);

        var (sessionId, analysisType) = validationResult.Value;
        var request = new PredefinedAnalysisRequest(sessionId, analysisType);

        var result = await _httpClient.PostAsync<PredefinedAnalysisRequest, CommandExecutionResponse>(
            ApiEndpoints.PredefinedAnalysis, request);

        return result.IsSuccess 
            ? McpToolResult.Success(result.Value.Result) 
            : McpToolResult.Error(result.Error);
    }

    public async Task<McpToolResult> ListSessionsAsync()
    {
        var result = await _httpClient.GetAsync<SessionsResponse>(ApiEndpoints.Sessions);
        if (result.IsFailure)
            return McpToolResult.Error(result.Error);

        var sessionList = new System.Text.StringBuilder();
        sessionList.AppendLine("Active sessions:");

        foreach (var session in result.Value.Sessions)
        {
            sessionList.AppendLine($"  Session ID: {session.SessionId}");
            sessionList.AppendLine($"    Dump File: {session.DumpFile}");
            sessionList.AppendLine($"    Active: {session.IsActive}");
            sessionList.AppendLine();
        }

        return McpToolResult.Success(sessionList.ToString());
    }

    public async Task<McpToolResult> CloseSessionAsync(JsonElement args)
    {
        var validationResult = _validationService.ValidateSessionId(args);
        if (validationResult.IsFailure)
            return McpToolResult.Error(validationResult.Error);

        var result = await _httpClient.DeleteAsync<CloseSessionResponse>(
            ApiEndpoints.SessionById(validationResult.Value));

        return result.IsSuccess 
            ? McpToolResult.Success(result.Value.Message) 
            : McpToolResult.Error(result.Error);
    }

    public async Task<McpToolResult> DetectDebuggersAsync()
    {
        var result = await _httpClient.GetAsync<DebuggerDetectionResponse>(ApiEndpoints.DetectDebuggers);
        if (result.IsFailure)
            return McpToolResult.Error(result.Error);

        var response = result.Value;
        var output = new System.Text.StringBuilder();
        output.AppendLine("🔍 Debugger Detection Results:");
        output.AppendLine();

        if (!string.IsNullOrEmpty(response.CdbPath))
            output.AppendLine($"✅ Primary debugger: {response.CdbPath}");
        else
            output.AppendLine("❌ No CDB found");

        if (!string.IsNullOrEmpty(response.WinDbgPath) && response.WinDbgPath != response.CdbPath)
            output.AppendLine($"📊 WinDbg available: {response.WinDbgPath}");

        output.AppendLine();
        output.AppendLine("🔧 Environment variables:");

        foreach (var envVar in response.EnvironmentVariables)
        {
            var value = envVar.Value ?? "(not set)";
            output.AppendLine($"  {envVar.Key}: {value}");
        }

        return McpToolResult.Success(output.ToString());
    }

    public async Task<McpToolResult> ListAnalysesAsync()
    {
        var result = await _httpClient.GetAsync<AnalysesResponse>(ApiEndpoints.Analyses);
        if (result.IsFailure)
            return McpToolResult.Error(result.Error);

        var output = new System.Text.StringBuilder();
        output.AppendLine("Available predefined analyses:");
        output.AppendLine();

        foreach (var analysis in result.Value.Analyses)
        {
            output.AppendLine($"{analysis.Name}: {analysis.Description}");
        }

        return McpToolResult.Success(output.ToString());
    }
}