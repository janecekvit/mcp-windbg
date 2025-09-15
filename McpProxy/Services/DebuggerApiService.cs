using System.Text;
using System.Text.Json;
using McpProxy.Extensions;
using McpProxy.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Shared.Extensions;
using Shared.Models;

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
        ICommunicationService communicationService,
        IConfiguration configuration)
    {
        _logger = logger;
        _httpClient = httpClient;
        _communicationService = communicationService;

        var backgroundServiceConfig = configuration.GetBackgroundServiceConfiguration();
        _baseUrl = backgroundServiceConfig.BaseUrl;

        _logger.LogInformation("Configured API client for: {BaseUrl}", _baseUrl);
    }

    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}{ApiEndpoints.Health}", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
            return false;
        }
    }

    private async Task SendProgress(string? token, double progress, string message, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(token))
            await _communicationService.SendProgressNotificationAsync(token, progress, message, cancellationToken);
    }

    public async Task<McpToolResult> LoadDumpAsync(JsonElement args, string? progressToken = null, CancellationToken cancellationToken = default)
    {
        var pathResult = args.GetRequiredString("dump_file_path");
        if (pathResult.IsFailure) return McpToolResult.Error(pathResult.Error);

        var asyncResult = args.TryGetBool("async");
        var useAsync = asyncResult.IsSuccess && asyncResult.Value;

        var dumpFilePath = pathResult.Value;
        var error = dumpFilePath.ValidateAsDumpFilePath();
        if (error != null) return McpToolResult.Error(error);

        if (useAsync)
        {
            var request = new LoadDumpRequest(dumpFilePath!);
            var response = await PostAsync<LoadDumpRequest, BackgroundTaskResponse>(ApiEndpoints.AsyncLoadDump, request, cancellationToken);
            return McpToolResult.Success($"Background task started: {response.TaskId}\n{response.Message}\n\nUse task ID to check progress.");
        }
        else
        {
        await SendProgress(progressToken, 0.1, "Loading dump file...", cancellationToken);
        var request = new LoadDumpRequest(dumpFilePath!);

        var response = await PostAsync<LoadDumpRequest, LoadDumpResponse>(ApiEndpoints.LoadDump, request, cancellationToken);

        await SendProgress(progressToken, 1.0, "Dump loaded successfully!", cancellationToken);
        return McpToolResult.Success($"Session created: {response.SessionId}\nDump: {dumpFilePath}\n\n{response.Message}");
    }
    }

    public async Task<McpToolResult> ExecuteCommandAsync(JsonElement args, string? progressToken = null, CancellationToken cancellationToken = default)
    {
        var paramsResult = args.GetRequiredStrings("session_id", "command");
        if (paramsResult.IsFailure) return McpToolResult.Error(paramsResult.Error);

        var asyncResult = args.TryGetBool("async");
        var useAsync = asyncResult.IsSuccess && asyncResult.Value;

        var (sessionId, command) = paramsResult.Value;

        var sessionError = sessionId.ValidateAsSessionId();
        if (sessionError != null) return McpToolResult.Error(sessionError);

        var cmdError = command.ValidateAsCommand();
        if (cmdError != null) return McpToolResult.Error(cmdError);

        if (useAsync)
        {
        var request = new ExecuteCommandRequest(sessionId!, command!);
            var response = await PostAsync<ExecuteCommandRequest, BackgroundTaskResponse>(ApiEndpoints.AsyncExecuteCommand, request, cancellationToken);
            return McpToolResult.Success($"Background task started: {response.TaskId}\n{response.Message}\n\nUse task ID to check progress.");
        }
        else
        {
            var request = new ExecuteCommandRequest(sessionId!, command!);
        var response = await PostAsync<ExecuteCommandRequest, CommandExecutionResponse>(ApiEndpoints.ExecuteCommand, request, cancellationToken);

        return McpToolResult.Success(response.Result);
    }
    }

    public async Task<McpToolResult> BasicAnalysisAsync(JsonElement args, string? progressToken = null, CancellationToken cancellationToken = default)
    {
        var sessionResult = args.GetRequiredString("session_id");
        if (sessionResult.IsFailure) return McpToolResult.Error(sessionResult.Error);

        var asyncResult = args.TryGetBool("async");
        var useAsync = asyncResult.IsSuccess && asyncResult.Value;

        var sessionId = sessionResult.Value;
        var error = sessionId.ValidateAsSessionId();
        if (error != null) return McpToolResult.Error(error);

        if (useAsync)
        {
            var request = new BasicAnalysisRequest(sessionId!);
            var response = await PostAsync<BasicAnalysisRequest, BackgroundTaskResponse>(ApiEndpoints.AsyncBasicAnalysis, request, cancellationToken);
            return McpToolResult.Success($"Background task started: {response.TaskId}\n{response.Message}\n\nUse task ID to check progress.");
        }
        else
        {
        await SendProgress(progressToken, 0.1, "Running analysis...", cancellationToken);
        var request = new BasicAnalysisRequest(sessionId!);

        var response = await PostAsync<BasicAnalysisRequest, CommandExecutionResponse>(ApiEndpoints.BasicAnalysis, request, cancellationToken);

        await SendProgress(progressToken, 1.0, "Analysis completed!", cancellationToken);
        return McpToolResult.Success(response.Result);
    }
    }

    public async Task<McpToolResult> PredefinedAnalysisAsync(JsonElement args, string? progressToken = null, CancellationToken cancellationToken = default)
    {
        var paramsResult = args.GetRequiredStrings("session_id", "analysis_type");
        if (paramsResult.IsFailure) return McpToolResult.Error(paramsResult.Error);

        var asyncResult = args.TryGetBool("async");
        var useAsync = asyncResult.IsSuccess && asyncResult.Value;

        var (sessionId, analysisType) = paramsResult.Value;

        var sessionError = sessionId.ValidateAsSessionId();
        if (sessionError != null) return McpToolResult.Error(sessionError);

        if (useAsync)
        {
            var request = new PredefinedAnalysisRequest(sessionId!, analysisType!);
            var response = await PostAsync<PredefinedAnalysisRequest, BackgroundTaskResponse>(ApiEndpoints.AsyncPredefinedAnalysis, request, cancellationToken);
            return McpToolResult.Success($"Background task started: {response.TaskId}\n{response.Message}\n\nUse task ID to check progress.");
        }
        else
        {
        var request = new PredefinedAnalysisRequest(sessionId!, analysisType!);
        var response = await PostAsync<PredefinedAnalysisRequest, CommandExecutionResponse>(ApiEndpoints.PredefinedAnalysis, request, cancellationToken);

        return McpToolResult.Success(response.Result);
    }
    }

    public async Task<McpToolResult> ListSessionsAsync(CancellationToken cancellationToken = default)
    {
        var response = await GetAsync<SessionsResponse>(ApiEndpoints.Sessions, cancellationToken);

        var output = new StringBuilder()
            .AppendSection("Active sessions:");

        foreach (var session in response.Sessions)
        {
            output.AppendKeyValue("Session", session.SessionId)
                  .AppendKeyValue("Dump", session.DumpFile)
                  .AppendKeyValue("Active", session.IsActive)
                  .AppendLine();
        }
        return output.ToMcpSuccess();
    }

    public async Task<McpToolResult> CloseSessionAsync(JsonElement args, CancellationToken cancellationToken = default)
    {
        var sessionResult = args.GetRequiredString("session_id");
        if (sessionResult.IsFailure) return McpToolResult.Error(sessionResult.Error);

        var sessionId = sessionResult.Value;
        var error = sessionId.ValidateAsSessionId();
        if (error != null) return McpToolResult.Error(error);

        var response = await DeleteAsync<CloseSessionResponse>(sessionId!.ToSessionEndpoint(), cancellationToken);
        return McpToolResult.Success(response.Message);
    }

    public async Task<McpToolResult> DetectDebuggersAsync(CancellationToken cancellationToken = default)
    {
        var response = await GetAsync<DebuggerDetectionResponse>(ApiEndpoints.DetectDebuggers, cancellationToken);

        var output = new StringBuilder()
            .AppendSection("🔍 Debugger Detection:");

        if (!string.IsNullOrEmpty(response.CdbPath))
            output.AppendLine($"✅ CDB: {response.CdbPath}");
        else
            output.AppendLine("❌ No CDB found");

        if (!string.IsNullOrEmpty(response.WinDbgPath) && response.WinDbgPath != response.CdbPath)
            output.AppendLine($"📊 WinDbg: {response.WinDbgPath}");

        output.AppendSection("🔧 Environment:");
        foreach (var env in response.EnvironmentVariables)
            output.AppendKeyValue(env.Key, env.Value ?? "(not set)");

        return output.ToMcpSuccess();
    }

    public async Task<McpToolResult> ListAnalysesAsync(CancellationToken cancellationToken = default)
    {
        var response = await GetAsync<AnalysesResponse>(ApiEndpoints.Analyses, cancellationToken);

        var output = new StringBuilder()
            .AppendSection("Available analyses:");

        foreach (var analysis in response.Analyses)
            output.AppendKeyValue(analysis.Name, analysis.Description);

        return output.ToMcpSuccess();
    }

    public async Task<McpToolResult> GetTaskStatusAsync(JsonElement args, CancellationToken cancellationToken = default)
    {
        var taskIdResult = args.GetRequiredString("task_id");
        if (taskIdResult.IsFailure) return McpToolResult.Error(taskIdResult.Error);

        var taskId = taskIdResult.Value;
        if (string.IsNullOrWhiteSpace(taskId)) return McpToolResult.Error("Task ID is required");

        var response = await GetAsync<BackgroundTaskInfo>($"{ApiEndpoints.AsyncTasks}/{taskId}", cancellationToken);

        var output = new StringBuilder()
            .AppendSection($"Task: {response.TaskId}")
            .AppendKeyValue("Type", response.Type.ToString())
            .AppendKeyValue("Status", response.Status.ToString())
            .AppendKeyValue("Description", response.Description)
            .AppendKeyValue("Started", response.StartedAt.ToString("yyyy-MM-dd HH:mm:ss"));

        if (response.CompletedAt.HasValue)
            output.AppendKeyValue("Completed", response.CompletedAt.Value.ToString("yyyy-MM-dd HH:mm:ss"));

        if (!string.IsNullOrEmpty(response.SessionId))
            output.AppendKeyValue("Session", response.SessionId);

        if (!string.IsNullOrEmpty(response.Error))
            output.AppendKeyValue("Error", response.Error);

        if (!string.IsNullOrEmpty(response.Result))
        {
            output.AppendSection("Result:");
            output.AppendLine(response.Result);
        }

        return output.ToMcpSuccess();
    }

    public async Task<McpToolResult> ListBackgroundTasksAsync(CancellationToken cancellationToken = default)
    {
        var response = await GetAsync<BackgroundTaskListResponse>(ApiEndpoints.AsyncTasks, cancellationToken);

        var output = new StringBuilder()
            .AppendSection("Background tasks:");

        foreach (var task in response.Tasks)
        {
            output.AppendKeyValue("Task", task.TaskId)
                  .AppendKeyValue("Type", task.Type.ToString())
                  .AppendKeyValue("Status", task.Status.ToString())
                  .AppendKeyValue("Started", task.StartedAt.ToString("yyyy-MM-dd HH:mm:ss"));

            if (!string.IsNullOrEmpty(task.SessionId))
                output.AppendKeyValue("Session", task.SessionId);

            output.AppendLine();
        }

        return output.ToMcpSuccess();
    }

    public async Task<McpToolResult> CancelTaskAsync(JsonElement args, CancellationToken cancellationToken = default)
    {
        var taskIdResult = args.GetRequiredString("task_id");
        if (taskIdResult.IsFailure) return McpToolResult.Error(taskIdResult.Error);

        var taskId = taskIdResult.Value;
        if (string.IsNullOrWhiteSpace(taskId)) return McpToolResult.Error("Task ID is required");

        var response = await DeleteAsync<BackgroundTaskResponse>($"{ApiEndpoints.AsyncTasks}/{taskId}", cancellationToken);
        return McpToolResult.Success(response.Message);
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string endpoint, TRequest request, CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class
    {
        try
        {
            var json = JsonSerializer.Serialize(request, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_baseUrl}{endpoint}", content, cancellationToken);

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

    private async Task<TResponse> GetAsync<TResponse>(string endpoint, CancellationToken cancellationToken = default) where TResponse : class
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}{endpoint}", cancellationToken);

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

    private async Task<TResponse> DeleteAsync<TResponse>(string endpoint, CancellationToken cancellationToken = default) where TResponse : class
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"{_baseUrl}{endpoint}", cancellationToken);

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