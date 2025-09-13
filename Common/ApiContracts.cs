using System.Text.Json.Serialization;

namespace Common;

// API Endpoints (for ASP.NET Core Controllers)
public static class ApiEndpoints
{
    public const string Health = "/api/diagnostics/health";
    public const string LoadDump = "/api/sessions/load-dump";
    public const string ExecuteCommand = "/api/sessions/execute-command";
    public const string BasicAnalysis = "/api/sessions/basic-analysis";
    public const string PredefinedAnalysis = "/api/sessions/predefined-analysis";
    public const string Sessions = "/api/sessions";
    public const string DetectDebuggers = "/api/diagnostics/detect-debuggers";
    public const string Analyses = "/api/diagnostics/analyses";
}

// Shared Request Models
public record LoadDumpRequest([property: JsonPropertyName("dumpFilePath")] string DumpFilePath);

public record ExecuteCommandRequest(
    [property: JsonPropertyName("sessionId")] string SessionId, 
    [property: JsonPropertyName("command")] string Command);

public record BasicAnalysisRequest([property: JsonPropertyName("sessionId")] string SessionId);

public record PredefinedAnalysisRequest(
    [property: JsonPropertyName("sessionId")] string SessionId, 
    [property: JsonPropertyName("analysisType")] string AnalysisType);

// Shared Response Models
public record LoadDumpResponse(
    [property: JsonPropertyName("sessionId")] string SessionId,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("dumpFile")] string? DumpFile = null);

public record CommandExecutionResponse([property: JsonPropertyName("result")] string Result);

public record SessionInfo(
    [property: JsonPropertyName("sessionId")] string SessionId,
    [property: JsonPropertyName("dumpFile")] string DumpFile,
    [property: JsonPropertyName("isActive")] bool IsActive);

public record SessionsResponse([property: JsonPropertyName("sessions")] IReadOnlyList<SessionInfo> Sessions);

public record AnalysisInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description);

public record AnalysesResponse([property: JsonPropertyName("analyses")] IReadOnlyList<AnalysisInfo> Analyses);

public record DebuggerDetectionResponse(
    [property: JsonPropertyName("cdbPath")] string? CdbPath,
    [property: JsonPropertyName("winDbgPath")] string? WinDbgPath,
    [property: JsonPropertyName("foundPaths")] IReadOnlyList<string> FoundPaths,
    [property: JsonPropertyName("environmentVariables")] Dictionary<string, string?> EnvironmentVariables);

public record CloseSessionResponse([property: JsonPropertyName("message")] string Message);

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
    /// Creates an API endpoint path for a specific session
    /// </summary>
    /// <param name="sessionId">The session ID</param>
    /// <returns>API endpoint path for the session</returns>
    public static string ToSessionEndpoint(this string sessionId) => $"/api/sessions/{sessionId}";
}