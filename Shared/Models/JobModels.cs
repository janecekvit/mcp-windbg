using System.Text.Json.Serialization;

namespace Shared.Models;

/// <summary>
/// Represents the current state of a job
/// </summary>
public enum JobState
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// Represents the current phase/stage of a job for detailed progress tracking
/// </summary>
public enum JobPhase
{
    Queued,
    ValidatingInput,
    StartingCdb,
    LoadingDump,
    ConfiguringSymbols,
    ResolvingSymbols,
    DownloadingSymbols,
    VerifyingSymbols,
    ExecutingCommand,
    Analyzing,
    Completed
}

/// <summary>
/// Represents the type of operation being performed
/// </summary>
public enum JobOperationType
{
    LoadDump,
    ExecuteCommand,
    BasicAnalysis,
    PredefinedAnalysis,
    BatchCommands,
    CloseSession
}

/// <summary>
/// Complete status information for a job
/// </summary>
public record JobStatus(
    [property: JsonPropertyName("jobId")] string JobId,
    [property: JsonPropertyName("sessionId")] string? SessionId,
    [property: JsonPropertyName("operation")] JobOperationType Operation,
    [property: JsonPropertyName("state")] JobState State,
    [property: JsonPropertyName("phase")] JobPhase Phase,
    [property: JsonPropertyName("progress")] double Progress,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt,
    [property: JsonPropertyName("startedAt")] DateTime? StartedAt,
    [property: JsonPropertyName("completedAt")] DateTime? CompletedAt,
    [property: JsonPropertyName("estimatedTimeRemaining")] TimeSpan? EstimatedTimeRemaining,
    [property: JsonPropertyName("result")] string? Result,
    [property: JsonPropertyName("error")] string? Error);

/// <summary>
/// Response returned when a new job is created
/// </summary>
public record JobCreatedResponse(
    [property: JsonPropertyName("jobId")] string JobId,
    [property: JsonPropertyName("statusEndpoint")] string StatusEndpoint,
    [property: JsonPropertyName("message")] string Message);

/// <summary>
/// Progress notification sent via SignalR
/// </summary>
public record ProgressNotification(
    [property: JsonPropertyName("jobId")] string JobId,
    [property: JsonPropertyName("progress")] double Progress,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("timestamp")] DateTime Timestamp);

/// <summary>
/// Job completion notification sent via SignalR
/// </summary>
public record JobCompletedNotification(
    [property: JsonPropertyName("jobId")] string JobId,
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("result")] string? Result,
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("timestamp")] DateTime Timestamp);

/// <summary>
/// Structured progress update instead of raw string messages
/// </summary>
public record ProgressUpdate(
    [property: JsonPropertyName("phase")] JobPhase Phase,
    [property: JsonPropertyName("progress")] double Progress,
    [property: JsonPropertyName("message")] string Message)
{
    /// <summary>
    /// Factory methods for common progress updates
    /// </summary>
    public static ProgressUpdate ValidatingInput(string message = "Validating input...")
        => new(JobPhase.ValidatingInput, 0.05, message);

    public static ProgressUpdate StartingCdb(string message = "Starting CDB debugger...")
        => new(JobPhase.StartingCdb, 0.15, message);

    public static ProgressUpdate LoadingDump(string message = "Loading memory dump...")
        => new(JobPhase.LoadingDump, 0.20, message);

    public static ProgressUpdate ConfiguringSymbols(string message = "Configuring symbol options...")
        => new(JobPhase.ConfiguringSymbols, 0.25, message);

    public static ProgressUpdate SettingSymbolPaths(string message = "Setting up symbol paths...")
        => new(JobPhase.ConfiguringSymbols, 0.30, message);

    public static ProgressUpdate ResolvingSymbols(string message = "Resolving symbols...")
        => new(JobPhase.ResolvingSymbols, 0.40, message);

    public static ProgressUpdate DownloadingSymbols(string message = "Downloading symbols from server...")
        => new(JobPhase.DownloadingSymbols, 0.60, message);

    public static ProgressUpdate LoadingFromCache(string message = "Loading symbols from cache...")
        => new(JobPhase.ResolvingSymbols, 0.70, message);

    public static ProgressUpdate VerifyingSymbols(string message = "Verifying symbol loading...")
        => new(JobPhase.VerifyingSymbols, 0.85, message);

    public static ProgressUpdate SessionReady(string message = "Session initialized successfully")
        => new(JobPhase.Completed, 0.95, message);

    public static ProgressUpdate ExecutingCommand(string commandName, double progress = 0.5)
        => new(JobPhase.ExecutingCommand, progress, $"Executing command: {commandName}");

    public static ProgressUpdate Analyzing(string analysisType, double progress = 0.5)
        => new(JobPhase.Analyzing, progress, $"Running {analysisType} analysis...");

    public static ProgressUpdate Completed(string message = "Operation completed")
        => new(JobPhase.Completed, 1.0, message);
}
