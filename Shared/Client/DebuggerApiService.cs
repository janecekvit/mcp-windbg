using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using Shared;
using Shared.Configuration;
using Shared.Models;

namespace Shared.Client;

/// <summary>
/// Service for interacting with the Dump Analysis Service HTTP API
/// Simplified implementation using MCP SDK progress reporting
/// </summary>
public class DebuggerApiService : IDebuggerApiService
{
    private readonly ILogger<DebuggerApiService> _logger;
    private readonly HttpClient _httpClient;
    private readonly ISignalRClientService _signalRClient;
    private readonly string _baseUrl;
    private readonly SymbolsConfiguration? _symbols;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public DebuggerApiService(
        ILogger<DebuggerApiService> logger,
        HttpClient httpClient,
        ISignalRClientService signalRClient,
        string baseUrl = Constants.Network.DefaultServiceUrl,
        SymbolsConfiguration? symbols = null)
    {
        _logger = logger;
        _httpClient = httpClient;
        _signalRClient = signalRClient;
        _baseUrl = baseUrl;
        _symbols = symbols;

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

    public async Task<string> LoadDumpAsync(
        string dumpFilePath,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Validate dump file path
        if (string.IsNullOrWhiteSpace(dumpFilePath))
            throw new ArgumentException("Dump file path is required", nameof(dumpFilePath));

        if (!File.Exists(dumpFilePath))
            throw new FileNotFoundException($"Dump file not found: {dumpFilePath}");

        var request = new LoadDumpRequest(dumpFilePath, _symbols);

        // Start the job
        var response = await _httpClient.PostAsJsonAsync(
            $"{_baseUrl}{ApiEndpoints.LoadDumpAsync}",
            request,
            _jsonOptions,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var jobCreated = await response.Content.ReadFromJsonAsync<JobCreatedResponse>(_jsonOptions, cancellationToken);
        if (jobCreated == null)
            throw new InvalidOperationException("Failed to parse job creation response");

        var jobId = jobCreated.JobId;
        _logger.LogInformation("Created job {JobId} for loading dump", jobId);

        // Subscribe to SignalR progress notifications and wait for completion
        return await _WaitForJobCompletionAsync(jobId, progress, cancellationToken);
    }

    public async Task<string> ExecuteCommandAsync(
        string sessionId,
        string command,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("Session ID is required", nameof(sessionId));

        if (string.IsNullOrWhiteSpace(command))
            throw new ArgumentException("Command is required", nameof(command));

        var request = new ExecuteCommandRequest(sessionId, command);

        var response = await _httpClient.PostAsJsonAsync(
            $"{_baseUrl}{ApiEndpoints.ExecuteCommandAsync}",
            request,
            _jsonOptions,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var jobCreated = await response.Content.ReadFromJsonAsync<JobCreatedResponse>(_jsonOptions, cancellationToken);
        if (jobCreated == null)
            throw new InvalidOperationException("Failed to parse job creation response");

        return await _WaitForJobCompletionAsync(jobCreated.JobId, progress, cancellationToken);
    }

    public async Task<string> BasicAnalysisAsync(
        string sessionId,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("Session ID is required", nameof(sessionId));

        var request = new BasicAnalysisRequest(sessionId);

        var response = await _httpClient.PostAsJsonAsync(
            $"{_baseUrl}{ApiEndpoints.BasicAnalysisAsync}",
            request,
            _jsonOptions,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var jobCreated = await response.Content.ReadFromJsonAsync<JobCreatedResponse>(_jsonOptions, cancellationToken);
        if (jobCreated == null)
            throw new InvalidOperationException("Failed to parse job creation response");

        return await _WaitForJobCompletionAsync(jobCreated.JobId, progress, cancellationToken);
    }

    public async Task<string> PredefinedAnalysisAsync(
        string sessionId,
        AnalysisType analysisType,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("Session ID is required", nameof(sessionId));

        var request = new PredefinedAnalysisRequest(sessionId, analysisType);

        var response = await _httpClient.PostAsJsonAsync(
            $"{_baseUrl}{ApiEndpoints.PredefinedAnalysisAsync}",
            request,
            _jsonOptions,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var jobCreated = await response.Content.ReadFromJsonAsync<JobCreatedResponse>(_jsonOptions, cancellationToken);
        if (jobCreated == null)
            throw new InvalidOperationException("Failed to parse job creation response");

        return await _WaitForJobCompletionAsync(jobCreated.JobId, progress, cancellationToken);
    }

    public async Task<string> CloseSessionAsync(
        string sessionId,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("Session ID is required", nameof(sessionId));

        var request = new CloseSessionRequest(sessionId);

        var response = await _httpClient.PostAsJsonAsync(
            $"{_baseUrl}{ApiEndpoints.CloseSessionAsync}",
            request,
            _jsonOptions,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var jobCreated = await response.Content.ReadFromJsonAsync<JobCreatedResponse>(_jsonOptions, cancellationToken);
        if (jobCreated == null)
            throw new InvalidOperationException("Failed to parse job creation response");

        return await _WaitForJobCompletionAsync(jobCreated.JobId, progress, cancellationToken);
    }

    public async Task<string> ListJobsAsync(string? state = null, CancellationToken cancellationToken = default)
    {
        var url = $"{_baseUrl}/api/jobs";
        if (!string.IsNullOrWhiteSpace(state))
            url += $"?state={Uri.EscapeDataString(state)}";

        var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var jobs = await response.Content.ReadFromJsonAsync<List<JobStatus>>(_jsonOptions, cancellationToken);
        if (jobs == null)
            throw new InvalidOperationException("Failed to parse jobs list");

        // Format as readable text
        var sb = new StringBuilder();
        sb.AppendLine($"📋 Jobs ({jobs.Count}):");
        sb.AppendLine();

        foreach (var job in jobs)
        {
            var statusEmoji = job.State switch
            {
                JobState.Queued => "⏳",
                JobState.Running => "🔄",
                JobState.Completed => "✅",
                JobState.Failed => "❌",
                JobState.Cancelled => "🚫",
                _ => "❓"
            };

            sb.AppendLine($"{statusEmoji} Job {job.JobId}:");
            sb.AppendLine($"  State: {job.State}");
            sb.AppendLine($"  Progress: {job.Progress:F1}%");
            if (!string.IsNullOrWhiteSpace(job.SessionId))
                sb.AppendLine($"  Session: {job.SessionId}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public async Task<string> DetectDebuggersAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"{_baseUrl}{ApiEndpoints.DetectDebuggers}", cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<DebuggerDetectionResponse>(_jsonOptions, cancellationToken);
        if (result == null)
            throw new InvalidOperationException("Failed to parse debugger detection response");

        // Format as readable text
        var sb = new StringBuilder();
        sb.AppendLine("🔍 Debugger Detection:");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(result.CdbPath))
            sb.AppendLine($"✅ CDB: {result.CdbPath}");
        else
            sb.AppendLine("❌ CDB: Not found");

        if (result.FoundPaths.Any())
        {
            sb.AppendLine();
            sb.AppendLine("Other detected paths:");
            foreach (var path in result.FoundPaths)
                sb.AppendLine($"  • {path}");
        }

        return sb.ToString();
    }

    public async Task<string> ListAnalysesAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"{_baseUrl}/api/diagnostics/analyses", cancellationToken);
        response.EnsureSuccessStatusCode();

        var analyses = await response.Content.ReadFromJsonAsync<List<AnalysisInfo>>(_jsonOptions, cancellationToken);
        if (analyses == null)
            throw new InvalidOperationException("Failed to parse analyses list");

        // Format as readable text
        var sb = new StringBuilder();
        sb.AppendLine($"📊 Available Analyses ({analyses.Count}):");
        sb.AppendLine();

        foreach (var analysis in analyses)
        {
            sb.AppendLine($"• {analysis.Name}");
            sb.AppendLine($"  {analysis.Description}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Waits for a job to complete, subscribing to SignalR progress notifications
    /// and forwarding them to the MCP progress reporter
    /// </summary>
    private async Task<string> _WaitForJobCompletionAsync(
        string jobId,
        IProgress<ProgressNotificationValue>? progress,
        CancellationToken cancellationToken)
    {
        // Subscribe to SignalR progress notifications for this job
        _signalRClient.SubscribeToJobProgress(jobId, notification =>
        {
            // Forward SignalR progress to MCP progress reporter
            progress?.Report(new ProgressNotificationValue
            {
                Progress = (float)notification.Progress,
                Total = 100.0f, // Dump Analysis Service reports 0-100
                Message = notification.Message
            });
        });

        try
        {
            // Poll job status until completion
            var pollInterval = TimeSpan.FromMilliseconds(Constants.Jobs.DefaultPollIntervalMs);
            var maxWaitTime = TimeSpan.FromMilliseconds(Constants.Jobs.DefaultMaxWaitTimeMs);
            var startTime = DateTime.UtcNow;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Check timeout
                if (DateTime.UtcNow - startTime > maxWaitTime)
                    throw new TimeoutException($"Job {jobId} timed out after {maxWaitTime.TotalMinutes} minutes");

                // Get job status
                var statusResponse = await _httpClient.GetAsync(
                    $"{_baseUrl}{jobId.ToJobEndpoint()}",
                    cancellationToken);

                statusResponse.EnsureSuccessStatusCode();

                var jobStatus = await statusResponse.Content.ReadFromJsonAsync<JobStatus>(_jsonOptions, cancellationToken);
                if (jobStatus == null)
                    throw new InvalidOperationException("Failed to parse job status");

                // Check if job is complete
                if (jobStatus.State == JobState.Completed)
                {
                    _logger.LogInformation("Job {JobId} completed successfully", jobId);
                    return jobStatus.Result ?? "Operation completed successfully";
                }

                if (jobStatus.State == JobState.Failed)
                {
                    var errorMessage = jobStatus.Error ?? "Unknown error";
                    _logger.LogError("Job {JobId} failed: {Error}", jobId, errorMessage);
                    throw new InvalidOperationException($"Job failed: {errorMessage}");
                }

                if (jobStatus.State == JobState.Cancelled)
                {
                    _logger.LogWarning("Job {JobId} was cancelled", jobId);
                    throw new OperationCanceledException($"Job {jobId} was cancelled");
                }

                // Wait before next poll
                await Task.Delay(pollInterval, cancellationToken);
            }
        }
        finally
        {
            // Unsubscribe from SignalR notifications
            _signalRClient.UnsubscribeFromJobProgress(jobId);
        }
    }
}
