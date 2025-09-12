using System.Text;
using System.Text.Json;
using Common;
using McpProxy.Models;
using Microsoft.Extensions.Logging;

namespace McpProxy.Services;

public class DebuggerApiService : IDebuggerApiService
{
    private readonly ILogger<DebuggerApiService> _logger;
    private readonly HttpClient _httpClient;
    private readonly ICommunicationService _communicationService;
    private readonly string _baseUrl;
    
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public DebuggerApiService(
        ILogger<DebuggerApiService> logger,
        HttpClient httpClient,
        ICommunicationService communicationService)
    {
        _logger = logger;
        _httpClient = httpClient;
        _communicationService = communicationService;
        _baseUrl = Environment.GetEnvironmentVariable("BACKGROUND_SERVICE_URL") ?? "http://localhost:8080";
        
        _logger.LogInformation("Configured API client for: {BaseUrl}", _baseUrl);
    }

    public async Task<bool> CheckHealthAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}{ApiEndpoints.Health}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
            return false;
        }
    }

    private async Task SendProgress(string? token, double progress, string message)
    {
        if (!string.IsNullOrEmpty(token))
            await _communicationService.SendProgressNotificationAsync(token, progress, message);
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
        
        var response = await PostAsync<LoadDumpRequest, LoadDumpResponse>(ApiEndpoints.LoadDump, request);
        
        await SendProgress(progressToken, 1.0, "Dump loaded successfully!");
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
        var response = await PostAsync<ExecuteCommandRequest, CommandExecutionResponse>(ApiEndpoints.ExecuteCommand, request);
        
        return McpToolResult.Success(response.Result);
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
        
        var response = await PostAsync<BasicAnalysisRequest, CommandExecutionResponse>(ApiEndpoints.BasicAnalysis, request);

        await SendProgress(progressToken, 1.0, "Analysis completed!");
        return McpToolResult.Success(response.Result);
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
        var response = await PostAsync<PredefinedAnalysisRequest, CommandExecutionResponse>(ApiEndpoints.PredefinedAnalysis, request);
        
        return McpToolResult.Success(response.Result);
    }

    public async Task<McpToolResult> ListSessionsAsync()
    {
        var response = await GetAsync<SessionsResponse>(ApiEndpoints.Sessions);

        var output = new System.Text.StringBuilder("Active sessions:\n");
        foreach (var session in response.Sessions)
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

        var response = await DeleteAsync<CloseSessionResponse>(ApiEndpoints.SessionById(sessionId!));
        return McpToolResult.Success(response.Message);
    }

    public async Task<McpToolResult> DetectDebuggersAsync()
    {
        var response = await GetAsync<DebuggerDetectionResponse>(ApiEndpoints.DetectDebuggers);

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
        var response = await GetAsync<AnalysesResponse>(ApiEndpoints.Analyses);

        var output = new System.Text.StringBuilder("Available analyses:\n\n");
        foreach (var analysis in response.Analyses)
            output.AppendLine($"{analysis.Name}: {analysis.Description}");

        return McpToolResult.Success(output.ToString());
    }
    
    private async Task<TResponse> PostAsync<TRequest, TResponse>(string endpoint, TRequest request) 
        where TRequest : class 
        where TResponse : class
    {
        try
        {
            var json = JsonSerializer.Serialize(request, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync($"{_baseUrl}{endpoint}", content);
            
            if (response.IsSuccessStatusCode)
            {
                var responseText = await response.Content.ReadAsStringAsync();
                var responseData = JsonSerializer.Deserialize<TResponse>(responseText, _jsonOptions);
                return responseData ?? throw new InvalidOperationException("Failed to deserialize response");
            }
            
            var errorText = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {errorText}");
        }
        catch (Exception ex) when (ex is not HttpRequestException)
        {
            _logger.LogError(ex, "POST request failed for endpoint: {Endpoint}", endpoint);
            throw new HttpRequestException($"Request failed: {ex.Message}", ex);
        }
    }

    private async Task<TResponse> GetAsync<TResponse>(string endpoint) where TResponse : class
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}{endpoint}");
            
            if (response.IsSuccessStatusCode)
            {
                var responseText = await response.Content.ReadAsStringAsync();
                var responseData = JsonSerializer.Deserialize<TResponse>(responseText, _jsonOptions);
                return responseData ?? throw new InvalidOperationException("Failed to deserialize response");
            }
            
            var errorText = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {errorText}");
        }
        catch (Exception ex) when (ex is not HttpRequestException)
        {
            _logger.LogError(ex, "GET request failed for endpoint: {Endpoint}", endpoint);
            throw new HttpRequestException($"Request failed: {ex.Message}", ex);
        }
    }

    private async Task<TResponse> DeleteAsync<TResponse>(string endpoint) where TResponse : class
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"{_baseUrl}{endpoint}");
            
            if (response.IsSuccessStatusCode)
            {
                var responseText = await response.Content.ReadAsStringAsync();
                var responseData = JsonSerializer.Deserialize<TResponse>(responseText, _jsonOptions);
                return responseData ?? throw new InvalidOperationException("Failed to deserialize response");
            }
            
            var errorText = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {errorText}");
        }
        catch (Exception ex) when (ex is not HttpRequestException)
        {
            _logger.LogError(ex, "DELETE request failed for endpoint: {Endpoint}", endpoint);
            throw new HttpRequestException($"Request failed: {ex.Message}", ex);
        }
    }
}