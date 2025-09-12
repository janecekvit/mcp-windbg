using System.Text.Json;
using McpProxy.Models;

namespace McpProxy.Services;

public interface IValidationService
{
    Result<string> ValidateSessionId(JsonElement args);
    Result<(string sessionId, string command)> ValidateExecuteCommand(JsonElement args);
    Result<string> ValidateDumpFilePath(JsonElement args);
    Result<(string sessionId, string analysisType)> ValidatePredefinedAnalysis(JsonElement args);
}