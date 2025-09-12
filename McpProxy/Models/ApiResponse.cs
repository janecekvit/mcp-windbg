using System.Text.Json.Serialization;

namespace McpProxy.Models;

public record LoadDumpResponse(
    [property: JsonPropertyName("sessionId")] string SessionId,
    [property: JsonPropertyName("message")] string Message);

public record CommandExecutionResponse([property: JsonPropertyName("result")] string Result);

public record SessionInfo(
    [property: JsonPropertyName("SessionId")] string SessionId,
    [property: JsonPropertyName("DumpFile")] string DumpFile,
    [property: JsonPropertyName("IsActive")] bool IsActive);

public record SessionsResponse([property: JsonPropertyName("sessions")] SessionInfo[] Sessions);

public record AnalysisInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description);

public record AnalysesResponse([property: JsonPropertyName("analyses")] AnalysisInfo[] Analyses);

public record DebuggerDetectionResponse(
    [property: JsonPropertyName("cdbPath")] string? CdbPath,
    [property: JsonPropertyName("winDbgPath")] string? WinDbgPath,
    [property: JsonPropertyName("environmentVariables")] Dictionary<string, string?> EnvironmentVariables);

public record CloseSessionResponse([property: JsonPropertyName("message")] string Message);