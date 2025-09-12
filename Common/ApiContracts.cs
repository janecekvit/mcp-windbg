using System.Text.Json.Serialization;

namespace Common;

// API Endpoints
public static class ApiEndpoints
{
    public const string Health = "/health";
    public const string LoadDump = "/api/load-dump";
    public const string ExecuteCommand = "/api/execute-command";
    public const string BasicAnalysis = "/api/basic-analysis";
    public const string PredefinedAnalysis = "/api/predefined-analysis";
    public const string Sessions = "/api/sessions";
    public const string DetectDebuggers = "/api/detect-debuggers";
    public const string Analyses = "/api/analyses";
    
    public static string SessionById(string sessionId) => $"/api/sessions/{sessionId}";
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

public record SessionsResponse([property: JsonPropertyName("sessions")] SessionInfo[] Sessions);

public record AnalysisInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description);

public record AnalysesResponse([property: JsonPropertyName("analyses")] AnalysisInfo[] Analyses);

public record DebuggerDetectionResponse(
    [property: JsonPropertyName("cdbPath")] string? CdbPath,
    [property: JsonPropertyName("winDbgPath")] string? WinDbgPath,
    [property: JsonPropertyName("foundPaths")] string[] FoundPaths,
    [property: JsonPropertyName("environmentVariables")] Dictionary<string, string?> EnvironmentVariables);

public record CloseSessionResponse([property: JsonPropertyName("message")] string Message);

public record ErrorResponse([property: JsonPropertyName("error")] string Error);

// Validation Helper
public static class ValidationHelper
{
    public static string? ValidateSessionId(string? sessionId) =>
        string.IsNullOrWhiteSpace(sessionId) ? "Session ID is required" : null;
    
    public static string? ValidateCommand(string? command) =>
        string.IsNullOrWhiteSpace(command) ? "Command is required" : null;
        
    public static string? ValidateDumpFilePath(string? dumpFilePath) =>
        string.IsNullOrWhiteSpace(dumpFilePath) ? "Dump file path is required" : null;
}