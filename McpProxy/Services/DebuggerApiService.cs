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
    private readonly ISignalRClientService _signalRClient;
    private readonly string _baseUrl;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public DebuggerApiService(
        ILogger<DebuggerApiService> logger,
        HttpClient httpClient,
        ICommunicationService communicationService,
        ISignalRClientService signalRClient,
        IConfiguration configuration)
    {
        _logger = logger;
        _httpClient = httpClient;
        _communicationService = communicationService;
        _signalRClient = signalRClient;

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

        var dumpFilePath = pathResult.Value;
        var error = dumpFilePath.ValidateAsDumpFilePath();
        if (error != null) return McpToolResult.Error(error);

        try
        {
            var request = new LoadDumpRequest(dumpFilePath!);

            // Create job and subscribe to progress
            var jobResponse = await PostAsync<LoadDumpRequest, JobCreatedResponse>(ApiEndpoints.LoadDumpAsync, request, cancellationToken);
            _logger.LogInformation("Created job {JobId} for loading dump", jobResponse.JobId);

            // Subscribe to SignalR progress updates (they will be automatically forwarded to Claude via progressToken)
            await _signalRClient.SubscribeToJobAsync(jobResponse.JobId, cancellationToken);

            // Wait for job completion by polling status
            var result = await WaitForJobCompletionAsync(jobResponse.JobId, cancellationToken);

            // Unsubscribe from progress updates
            await _signalRClient.UnsubscribeFromJobAsync(jobResponse.JobId, cancellationToken);

            if (result.State == JobState.Completed)
            {
                var sessionId = result.Result;
                return McpToolResult.Success($"Session created: {sessionId}\nDump: {dumpFilePath}\n\nSession is ready for commands.");
            }
            else
            {
                return McpToolResult.Error($"Failed to load dump: {result.Error}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading dump file");
            return McpToolResult.Error($"Error loading dump: {ex.Message}");
        }
    }

    /// <summary>
    /// Waits for a job to complete by polling its status
    /// </summary>
    private async Task<JobStatus> WaitForJobCompletionAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var pollIntervalMs = Shared.Constants.Jobs.DefaultPollIntervalMs;
        var maxWaitTimeMs = Shared.Constants.Jobs.DefaultMaxWaitTimeMs;

        var startTime = DateTime.UtcNow;

        while ((DateTime.UtcNow - startTime).TotalMilliseconds < maxWaitTimeMs)
        {
            try
            {
                var status = await GetAsync<JobStatus>(jobId.ToJobEndpoint(), cancellationToken);

                if (status.State == JobState.Completed || status.State == JobState.Failed || status.State == JobState.Cancelled)
                {
                    _logger.LogInformation("Job {JobId} finished with state {State}", jobId, status.State);
                    return status;
                }

                await Task.Delay(pollIntervalMs, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error polling job status for {JobId}", jobId);
                await Task.Delay(pollIntervalMs, cancellationToken);
            }
        }

        throw new TimeoutException($"Job {jobId} did not complete within {maxWaitTimeMs / 1000} seconds");
    }

    public async Task<McpToolResult> ExecuteCommandAsync(JsonElement args, string? progressToken = null, CancellationToken cancellationToken = default)
    {
        var paramsResult = args.GetRequiredStrings("session_id", "command");
        if (paramsResult.IsFailure) return McpToolResult.Error(paramsResult.Error);

        var (sessionId, command) = paramsResult.Value;

        var sessionError = sessionId.ValidateAsSessionId();
        if (sessionError != null) return McpToolResult.Error(sessionError);

        var cmdError = command.ValidateAsCommand();
        if (cmdError != null) return McpToolResult.Error(cmdError);

        try
        {
            var request = new ExecuteCommandRequest(sessionId!, command!);

            // Create job and subscribe to progress
            var jobResponse = await PostAsync<ExecuteCommandRequest, JobCreatedResponse>(ApiEndpoints.ExecuteCommandAsync, request, cancellationToken);
            _logger.LogInformation("Created job {JobId} for executing command", jobResponse.JobId);

            // Subscribe to SignalR progress updates
            await _signalRClient.SubscribeToJobAsync(jobResponse.JobId, cancellationToken);

            // Wait for job completion
            var result = await WaitForJobCompletionAsync(jobResponse.JobId, cancellationToken);

            // Unsubscribe from progress updates
            await _signalRClient.UnsubscribeFromJobAsync(jobResponse.JobId, cancellationToken);

            if (result.State == JobState.Completed)
            {
                return McpToolResult.Success(result.Result ?? "Command completed successfully");
            }
            else
            {
                return McpToolResult.Error($"Command failed: {result.Error}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing command");
            return McpToolResult.Error($"Error executing command: {ex.Message}");
        }
    }

    public async Task<McpToolResult> BasicAnalysisAsync(JsonElement args, string? progressToken = null, CancellationToken cancellationToken = default)
    {
        var sessionResult = args.GetRequiredString("session_id");
        if (sessionResult.IsFailure) return McpToolResult.Error(sessionResult.Error);

        var sessionId = sessionResult.Value;
        var error = sessionId.ValidateAsSessionId();
        if (error != null) return McpToolResult.Error(error);

        try
        {
            var request = new BasicAnalysisRequest(sessionId!);

            // Create job and subscribe to progress
            var jobResponse = await PostAsync<BasicAnalysisRequest, JobCreatedResponse>(ApiEndpoints.BasicAnalysisAsync, request, cancellationToken);
            _logger.LogInformation("Created job {JobId} for basic analysis", jobResponse.JobId);

            // Subscribe to SignalR progress updates
            await _signalRClient.SubscribeToJobAsync(jobResponse.JobId, cancellationToken);

            // Wait for job completion
            var result = await WaitForJobCompletionAsync(jobResponse.JobId, cancellationToken);

            // Unsubscribe from progress updates
            await _signalRClient.UnsubscribeFromJobAsync(jobResponse.JobId, cancellationToken);

            if (result.State == JobState.Completed)
            {
                return McpToolResult.Success(result.Result ?? "Analysis completed successfully");
            }
            else
            {
                return McpToolResult.Error($"Analysis failed: {result.Error}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running basic analysis");
            return McpToolResult.Error($"Error running analysis: {ex.Message}");
        }
    }

    public async Task<McpToolResult> PredefinedAnalysisAsync(JsonElement args, string? progressToken = null, CancellationToken cancellationToken = default)
    {
        var paramsResult = args.GetRequiredStrings("session_id", "analysis_type");
        if (paramsResult.IsFailure) return McpToolResult.Error(paramsResult.Error);

        var (sessionId, analysisType) = paramsResult.Value;

        var sessionError = sessionId.ValidateAsSessionId();
        if (sessionError != null) return McpToolResult.Error(sessionError);

        try
        {
            var request = new PredefinedAnalysisRequest(sessionId!, analysisType!);

            // Create job and subscribe to progress
            var jobResponse = await PostAsync<PredefinedAnalysisRequest, JobCreatedResponse>(ApiEndpoints.PredefinedAnalysisAsync, request, cancellationToken);
            _logger.LogInformation("Created job {JobId} for predefined analysis", jobResponse.JobId);

            // Subscribe to SignalR progress updates
            await _signalRClient.SubscribeToJobAsync(jobResponse.JobId, cancellationToken);

            // Wait for job completion
            var result = await WaitForJobCompletionAsync(jobResponse.JobId, cancellationToken);

            // Unsubscribe from progress updates
            await _signalRClient.UnsubscribeFromJobAsync(jobResponse.JobId, cancellationToken);

            if (result.State == JobState.Completed)
            {
                return McpToolResult.Success(result.Result ?? "Analysis completed successfully");
            }
            else
            {
                return McpToolResult.Error($"Analysis failed: {result.Error}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running predefined analysis");
            return McpToolResult.Error($"Error running analysis: {ex.Message}");
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