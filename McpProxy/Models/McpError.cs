using System.Text.Json.Serialization;
using static Shared.Constants;

namespace McpProxy.Models;

public class McpError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    public McpError()
    {
    }

    public McpError(int code, string message)
    {
        Code = code;
        Message = message;
    }

    // Standard JSON-RPC error codes
    public static McpError ParseError() => new(McpErrorCodes.ParseError, "Parse error");
    public static McpError InvalidRequest() => new(McpErrorCodes.InvalidRequest, "Invalid Request");
    public static McpError MethodNotFound() => new(McpErrorCodes.MethodNotFound, "Method not found");
    public static McpError InvalidParams() => new(McpErrorCodes.InvalidParams, "Invalid params");
    public static McpError InternalError() => new(McpErrorCodes.InternalError, "Internal error");
    public static McpError ServerNotInitialized() => new(McpErrorCodes.ServerNotInitialized, "Server not initialized");
    public static McpError Custom(int code, string message) => new(code, message);
}