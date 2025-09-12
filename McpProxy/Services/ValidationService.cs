using System.Text.Json;
using McpProxy.Models;

namespace McpProxy.Services;

public class ValidationService : IValidationService
{
    public Result<string> ValidateSessionId(JsonElement args)
    {
        if (!args.TryGetProperty("session_id", out var sessionIdElement))
            return Result.Failure<string>("Missing session_id parameter");

        var sessionId = sessionIdElement.GetString();
        if (string.IsNullOrWhiteSpace(sessionId))
            return Result.Failure<string>("Empty session_id parameter");

        return Result.Success(sessionId);
    }

    public Result<(string sessionId, string command)> ValidateExecuteCommand(JsonElement args)
    {
        var sessionIdResult = ValidateSessionId(args);
        if (sessionIdResult.IsFailure)
            return Result.Failure<(string, string)>(sessionIdResult.Error);

        if (!args.TryGetProperty("command", out var commandElement))
            return Result.Failure<(string, string)>("Missing command parameter");

        var command = commandElement.GetString();
        if (string.IsNullOrWhiteSpace(command))
            return Result.Failure<(string, string)>("Empty command parameter");

        return Result.Success((sessionIdResult.Value, command));
    }

    public Result<string> ValidateDumpFilePath(JsonElement args)
    {
        if (!args.TryGetProperty("dump_file_path", out var dumpFileElement))
            return Result.Failure<string>("Missing dump_file_path parameter");

        var dumpFilePath = dumpFileElement.GetString();
        if (string.IsNullOrWhiteSpace(dumpFilePath))
            return Result.Failure<string>("Empty dump_file_path parameter");

        return Result.Success(dumpFilePath);
    }

    public Result<(string sessionId, string analysisType)> ValidatePredefinedAnalysis(JsonElement args)
    {
        var sessionIdResult = ValidateSessionId(args);
        if (sessionIdResult.IsFailure)
            return Result.Failure<(string, string)>(sessionIdResult.Error);

        if (!args.TryGetProperty("analysis_type", out var analysisTypeElement))
            return Result.Failure<(string, string)>("Missing analysis_type parameter");

        var analysisType = analysisTypeElement.GetString();
        if (string.IsNullOrWhiteSpace(analysisType))
            return Result.Failure<(string, string)>("Empty analysis_type parameter");

        return Result.Success((sessionIdResult.Value, analysisType));
    }
}