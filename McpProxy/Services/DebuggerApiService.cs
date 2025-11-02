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
    /// Waits for a job to complete via SignalR completion notification
    /// </summary>
    private async Task<JobStatus> WaitForJobCompletionAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var timeout = TimeSpan.FromMilliseconds(Shared.Constants.Jobs.DefaultMaxWaitTimeMs);

        // Wait for SignalR completion notification (no polling)
        var completionNotification = await _signalRClient.WaitForJobCompletionAsync(jobId, timeout, cancellationToken);

        // Get final job status with result/error details
        var status = await GetAsync<JobStatus>(jobId.ToJobEndpoint(), cancellationToken);
        _logger.LogInformation("Job {JobId} finished with state {State}", jobId, status.State);

        return status;
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

    public async Task<McpToolResult> CloseSessionAsync(JsonElement args, CancellationToken cancellationToken = default)
    {
        try
        {
            var sessionResult = args.GetRequiredString("session_id");
            if (sessionResult.IsFailure) return McpToolResult.Error(sessionResult.Error);

            var sessionId = sessionResult.Value;
            var error = sessionId.ValidateAsSessionId();
            if (error != null) return McpToolResult.Error(error);

            var request = new CloseSessionRequest(sessionId!);

            // Create job and subscribe to progress
            var jobResponse = await PostAsync<CloseSessionRequest, JobCreatedResponse>(ApiEndpoints.CloseSessionAsync, request, cancellationToken);
            _logger.LogInformation("Created job {JobId} for closing session {SessionId}", jobResponse.JobId, sessionId);

            // Subscribe to SignalR progress updates
            await _signalRClient.SubscribeToJobAsync(jobResponse.JobId, cancellationToken);

            // Wait for job completion
            var result = await WaitForJobCompletionAsync(jobResponse.JobId, cancellationToken);

            // Unsubscribe from progress updates
            await _signalRClient.UnsubscribeFromJobAsync(jobResponse.JobId, cancellationToken);

            if (result.State == JobState.Completed)
            {
                return McpToolResult.Success($"Session {sessionId} closed successfully");
            }
            else
            {
                return McpToolResult.Error($"Failed to close session: {result.Error}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error closing session");
            return McpToolResult.Error($"Error closing session: {ex.Message}");
        }
    }

    public async Task<McpToolResult> ListJobsAsync(JsonElement args, CancellationToken cancellationToken = default)
    {
        try
        {
            // Optional state filter
            var stateFilter = args.TryGetProperty("state", out var stateProp) ? stateProp.GetString() : null;

            // Build endpoint with optional query parameter
            var endpoint = ApiEndpoints.Jobs;
            if (!string.IsNullOrEmpty(stateFilter))
            {
                endpoint += $"?state={stateFilter}";
            }

            var jobs = await GetAsync<IEnumerable<JobStatus>>(endpoint, cancellationToken);

            var output = new StringBuilder()
                .AppendSection($"Jobs{(stateFilter != null ? $" (state={stateFilter})" : "")}:");

            if (!jobs.Any())
            {
                output.AppendLine("No jobs found");
            }
            else
            {
                foreach (var job in jobs)
                {
                    output.AppendLine()
                          .AppendKeyValue("Job ID", job.JobId)
                          .AppendKeyValue("Operation", job.Operation.ToString())
                          .AppendKeyValue("State", job.State.ToString())
                          .AppendKeyValue("Phase", job.Phase.ToString())
                          .AppendKeyValue("Progress", $"{job.Progress:P0}");

                    if (job.SessionId != null)
                        output.AppendKeyValue("Session", job.SessionId);

                    if (job.Message != null)
                        output.AppendKeyValue("Message", job.Message);

                    if (job.Error != null)
                        output.AppendKeyValue("Error", job.Error);

                    output.AppendKeyValue("Created", job.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));

                    if (job.CompletedAt.HasValue)
                        output.AppendKeyValue("Completed", job.CompletedAt.Value.ToString("yyyy-MM-dd HH:mm:ss"));
                }
            }

            return output.ToMcpSuccess();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing jobs");
            return McpToolResult.Error($"Error listing jobs: {ex.Message}");
        }
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