using System.Text.Json.Serialization;

namespace McpProxy.Models;

public record LoadDumpRequest([property: JsonPropertyName("dumpFilePath")] string DumpFilePath);

public record ExecuteCommandRequest(
    [property: JsonPropertyName("sessionId")] string SessionId, 
    [property: JsonPropertyName("command")] string Command);

public record BasicAnalysisRequest([property: JsonPropertyName("sessionId")] string SessionId);

public record PredefinedAnalysisRequest(
    [property: JsonPropertyName("sessionId")] string SessionId, 
    [property: JsonPropertyName("analysisType")] string AnalysisType);