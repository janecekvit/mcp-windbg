using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Shared.Models;

// API Endpoints (for ASP.NET Core Controllers)
public static class ApiEndpoints
{
    // Diagnostic endpoints
    public const string Health = "/api/diagnostics/health";
    public const string DetectDebuggers = "/api/diagnostics/detect-debuggers";
    public const string Analyses = "/api/diagnostics/analyses";

    // Job management endpoints (async with progress reporting via SignalR)
    public const string Jobs = "/api/jobs";
    public const string JobStatus = "/api/jobs/{jobId}";
    public const string LoadDumpAsync = "/api/jobs/load-dump";
    public const string ExecuteCommandAsync = "/api/jobs/execute-command";
    public const string BasicAnalysisAsync = "/api/jobs/basic-analysis";
    public const string PredefinedAnalysisAsync = "/api/jobs/predefined-analysis";
    public const string CloseSessionAsync = "/api/jobs/close-session";
}

// Shared Request Models
public record LoadDumpRequest(
    [Required(ErrorMessage = "Dump file path is required")]
    [property: JsonPropertyName("dumpFilePath")]
    string DumpFilePath);

public record ExecuteCommandRequest(
    [Required(ErrorMessage = "Session ID is required")]
    [property: JsonPropertyName("sessionId")]
    string SessionId,
    [Required(ErrorMessage = "Command is required")]
    [property: JsonPropertyName("command")]
    string Command);

public record BasicAnalysisRequest(
    [Required(ErrorMessage = "Session ID is required")]
    [property: JsonPropertyName("sessionId")]
    string SessionId);

public record PredefinedAnalysisRequest(
    [Required(ErrorMessage = "Session ID is required")]
    [property: JsonPropertyName("sessionId")]
    string SessionId,
    [Required(ErrorMessage = "Analysis type is required")]
    [property: JsonPropertyName("analysisType")]
    string AnalysisType);

public record CloseSessionRequest(
    [Required(ErrorMessage = "Session ID is required")]
    [property: JsonPropertyName("sessionId")]
    string SessionId);

// Shared Response Models
public record LoadDumpResponse(
    [property: JsonPropertyName("sessionId")] string SessionId,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("dumpFile")] string? DumpFile = null);

public record CommandExecutionResponse([property: JsonPropertyName("result")] string Result);

public record AnalysisInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description);

public record AnalysesResponse([property: JsonPropertyName("analyses")] IReadOnlyList<AnalysisInfo> Analyses);

public record DebuggerDetectionResponse(
    [property: JsonPropertyName("cdbPath")] string? CdbPath,
    [property: JsonPropertyName("foundPaths")] IReadOnlyList<string> FoundPaths,
    [property: JsonPropertyName("environmentVariables")] Dictionary<string, string?> EnvironmentVariables);

public record ErrorResponse([property: JsonPropertyName("error")] string Error);

// Extensions
public static class StringValidationExtensions
{
    public static string? ValidateAsSessionId(this string? sessionId) =>
        string.IsNullOrWhiteSpace(sessionId) ? "Session ID is required" : null;

    public static string? ValidateAsCommand(this string? command) =>
        string.IsNullOrWhiteSpace(command) ? "Command is required" : null;

    public static string? ValidateAsDumpFilePath(this string? dumpFilePath) =>
        string.IsNullOrWhiteSpace(dumpFilePath) ? "Dump file path is required" : null;
}

public static class ApiEndpointExtensions
{
    /// <summary>
    /// Creates an API endpoint path for a specific job
    /// </summary>
    /// <param name="jobId">The job ID</param>
    /// <returns>API endpoint path for the job</returns>
    public static string ToJobEndpoint(this string jobId) => $"/api/jobs/{jobId}";
}