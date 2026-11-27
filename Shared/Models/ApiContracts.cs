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
    public const string SetSymbolsAsync = "/api/jobs/set-symbols";
}

// Shared Request Models
public record LoadDumpRequest(
    [Required(ErrorMessage = "Dump file path is required")]
    [property: JsonPropertyName("dumpFilePath")]
    string DumpFilePath,

    // Optional: Symbol configuration (per-session from MCP server)
    [property: JsonPropertyName("symbols")]
    Configuration.SymbolsConfiguration? Symbols = null);

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
    AnalysisType AnalysisType);

public record SetSymbolsRequest(
    [Required(ErrorMessage = "Session ID is required")]
    [property: JsonPropertyName("sessionId")]
    string SessionId,
    // Omitted values fall back to the server defaults (Debugger:DefaultSymbolPathExtra, DefaultSymbolServers).
    [property: JsonPropertyName("symbolPathExtra")]
    string? SymbolPathExtra = null,
    [property: JsonPropertyName("symbolServers")]
    string? SymbolServers = null);

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
    [property: JsonPropertyName("foundPaths")] IReadOnlyList<string> FoundPaths);

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
/// <summary>Structured result of the load_dump MCP tool (its text content stays the bare session id).</summary>
public record LoadDumpResult(
    [property: JsonPropertyName("sessionId")] string SessionId,
    [property: JsonPropertyName("dumpPath")] string DumpPath,
    // "user-full", "user-mini" or "kernel"; null when the header could not be read
    [property: JsonPropertyName("dumpKind")] string? DumpKind,
    // "x64", "x86", "arm64", ...; null when unknown
    [property: JsonPropertyName("architecture")] string? Architecture);
