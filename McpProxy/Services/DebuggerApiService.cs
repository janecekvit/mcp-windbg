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

    private async Task _SendProgress(string? token, double progress, string message, CancellationToken cancellationToken = default)
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

        string? jobId = null;
        try
        {
            // Read symbol configuration from environment variables (set per MCP server instance)
            var symbols = new Shared.Configuration.SymbolsConfiguration(
                SymbolCache: Environment.GetEnvironmentVariable("SYMBOL_CACHE"),
                SymbolPathExtra: Environment.GetEnvironmentVariable("SYMBOL_PATH_EXTRA"),
                SymbolServers: Environment.GetEnvironmentVariable("SYMBOL_SERVERS"));

            var request = new LoadDumpRequest(dumpFilePath!, symbols);

            // Create job and subscribe to progress
            var jobResponse = await _PostAsync<LoadDumpRequest, JobCreatedResponse>(ApiEndpoints.LoadDumpAsync, request, cancellationToken);
            jobId = jobResponse.JobId;
            _logger.LogInformation("Created job {JobId} for loading dump", jobId);

            // Subscribe to SignalR progress updates (they will be automatically forwarded to Claude via progressToken)
            await _signalRClient.SubscribeToJobAsync(jobId, cancellationToken);

            // Wait for job completion by polling status
            var result = await _WaitForJobCompletionAsync(jobId, cancellationToken);

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
        finally
        {
            await _UnsubscribeFromJobAsync(jobId, cancellationToken);
        }
    }

    /// <summary>
    /// Waits for a job to complete via SignalR completion notification
    /// </summary>
    private async Task<JobStatus> _WaitForJobCompletionAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var timeout = TimeSpan.FromMilliseconds(Shared.Constants.Jobs.DefaultMaxWaitTimeMs);

        // Wait for SignalR completion notification (no polling)
        var completionNotification = await _signalRClient.WaitForJobCompletionAsync(jobId, timeout, cancellationToken);

        // Get final job status with result/error details
        var status = await _GetAsync<JobStatus>(jobId.ToJobEndpoint(), cancellationToken);
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

        string? jobId = null;
        try
        {
            var request = new ExecuteCommandRequest(sessionId!, command!);

            // Create job and subscribe to progress
            var jobResponse = await _PostAsync<ExecuteCommandRequest, JobCreatedResponse>(ApiEndpoints.ExecuteCommandAsync, request, cancellationToken);
            jobId = jobResponse.JobId;
            _logger.LogInformation("Created job {JobId} for executing command", jobId);

            // Subscribe to SignalR progress updates
            await _signalRClient.SubscribeToJobAsync(jobId, cancellationToken);

            // Wait for job completion
            var result = await _WaitForJobCompletionAsync(jobId, cancellationToken);

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
        finally
        {
            await _UnsubscribeFromJobAsync(jobId, cancellationToken);
        }
    }

    public async Task<McpToolResult> BasicAnalysisAsync(JsonElement args, string? progressToken = null, CancellationToken cancellationToken = default)
    {
        var sessionResult = args.GetRequiredString("session_id");
        if (sessionResult.IsFailure) return McpToolResult.Error(sessionResult.Error);

        var sessionId = sessionResult.Value;
        var error = sessionId.ValidateAsSessionId();
        if (error != null) return McpToolResult.Error(error);

        string? jobId = null;
        try
        {
            var request = new BasicAnalysisRequest(sessionId!);

            // Create job and subscribe to progress
            var jobResponse = await _PostAsync<BasicAnalysisRequest, JobCreatedResponse>(ApiEndpoints.BasicAnalysisAsync, request, cancellationToken);
            jobId = jobResponse.JobId;
            _logger.LogInformation("Created job {JobId} for basic analysis", jobId);

            // Subscribe to SignalR progress updates
            await _signalRClient.SubscribeToJobAsync(jobId, cancellationToken);

            // Wait for job completion
            var result = await _WaitForJobCompletionAsync(jobId, cancellationToken);

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
        finally
        {
            await _UnsubscribeFromJobAsync(jobId, cancellationToken);
        }
    }

    public async Task<McpToolResult> PredefinedAnalysisAsync(JsonElement args, string? progressToken = null, CancellationToken cancellationToken = default)
    {
        var paramsResult = args.GetRequiredStrings("session_id", "analysis_type");
        if (paramsResult.IsFailure) return McpToolResult.Error(paramsResult.Error);

        var (sessionId, analysisTypeString) = paramsResult.Value;

        var sessionError = sessionId.ValidateAsSessionId();
        if (sessionError != null) return McpToolResult.Error(sessionError);

        // Parse analysis type string to enum
        AnalysisType analysisType;
        try
        {
            analysisType = analysisTypeString!.ToAnalysisType();
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("Invalid analysis type from MCP: {AnalysisType}", analysisTypeString);
            return McpToolResult.Error(ex.Message);
        }

        string? jobId = null;
        try
        {
            var request = new PredefinedAnalysisRequest(sessionId!, analysisType);

            // Create job and subscribe to progress
            var jobResponse = await _PostAsync<PredefinedAnalysisRequest, JobCreatedResponse>(ApiEndpoints.PredefinedAnalysisAsync, request, cancellationToken);
            jobId = jobResponse.JobId;
            _logger.LogInformation("Created job {JobId} for predefined analysis", jobId);

            // Subscribe to SignalR progress updates
            await _signalRClient.SubscribeToJobAsync(jobId, cancellationToken);

            // Wait for job completion
            var result = await _WaitForJobCompletionAsync(jobId, cancellationToken);

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
        finally
        {
            await _UnsubscribeFromJobAsync(jobId, cancellationToken);
        }
    }

    public async Task<McpToolResult> CloseSessionAsync(JsonElement args, CancellationToken cancellationToken = default)
    {
        var sessionResult = args.GetRequiredString("session_id");
        if (sessionResult.IsFailure) return McpToolResult.Error(sessionResult.Error);

        var sessionId = sessionResult.Value;
        var error = sessionId.ValidateAsSessionId();
        if (error != null) return McpToolResult.Error(error);

        string? jobId = null;
        try
        {
            var request = new CloseSessionRequest(sessionId!);

            // Create job and subscribe to progress
            var jobResponse = await _PostAsync<CloseSessionRequest, JobCreatedResponse>(ApiEndpoints.CloseSessionAsync, request, cancellationToken);
            jobId = jobResponse.JobId;
            _logger.LogInformation("Created job {JobId} for closing session {SessionId}", jobId, sessionId);

            // Subscribe to SignalR progress updates
            await _signalRClient.SubscribeToJobAsync(jobId, cancellationToken);

            // Wait for job completion
            var result = await _WaitForJobCompletionAsync(jobId, cancellationToken);

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
        finally
        {
            await _UnsubscribeFromJobAsync(jobId, cancellationToken);
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

            var jobs = await _GetAsync<IEnumerable<JobStatus>>(endpoint, cancellationToken);

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
        var response = await _GetAsync<DebuggerDetectionResponse>(ApiEndpoints.DetectDebuggers, cancellationToken);

        var output = new StringBuilder()
            .AppendSection("🔍 Debugger Detection:");

        if (!string.IsNullOrEmpty(response.CdbPath))
            output.AppendLine($"✅ CDB: {response.CdbPath}");
        else
            output.AppendLine("❌ No CDB found");

        return output.ToMcpSuccess();
    }

    public async Task<McpToolResult> ListAnalysesAsync(CancellationToken cancellationToken = default)
    {
        var response = await _GetAsync<AnalysesResponse>(ApiEndpoints.Analyses, cancellationToken);

        var output = new StringBuilder()
            .AppendSection("Available analyses:");

        foreach (var analysis in response.Analyses)
            output.AppendKeyValue(analysis.Name, analysis.Description);

        return output.ToMcpSuccess();
    }

    private async Task<TResponse> _PostAsync<TRequest, TResponse>(string endpoint, TRequest request, CancellationToken cancellationToken = default)
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

    private async Task<TResponse> _GetAsync<TResponse>(string endpoint, CancellationToken cancellationToken = default) where TResponse : class
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

    private async Task<TResponse> _DeleteAsync<TResponse>(string endpoint, CancellationToken cancellationToken = default) where TResponse : class
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

    private async Task _UnsubscribeFromJobAsync(string? jobId, CancellationToken cancellationToken = default)
    {
        // Always unsubscribe from progress updates, even on error
        if (jobId != null)
        {
            try
            {
                await _signalRClient.UnsubscribeFromJobAsync(jobId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to unsubscribe from job {JobId}", jobId);
            }
        }
    }
}