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
/// Represents the type of operation being performed
/// </summary>
public enum JobOperationType
{
    LoadDump,
    ExecuteCommand,
    BasicAnalysis,
    PredefinedAnalysis,
    BatchCommands
}

/// <summary>
/// Complete status information for a job
/// </summary>
public record JobStatus(
    [property: JsonPropertyName("jobId")] string JobId,
    [property: JsonPropertyName("sessionId")] string? SessionId,
    [property: JsonPropertyName("operation")] JobOperationType Operation,
    [property: JsonPropertyName("state")] JobState State,
    [property: JsonPropertyName("progress")] double Progress,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt,
    [property: JsonPropertyName("startedAt")] DateTime? StartedAt,
    [property: JsonPropertyName("completedAt")] DateTime? CompletedAt,
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
