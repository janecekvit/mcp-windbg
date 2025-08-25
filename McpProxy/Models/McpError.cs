using System.Text.Json.Serialization;

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
    public static McpError ParseError() => new(-32700, "Parse error");
    public static McpError InvalidRequest() => new(-32600, "Invalid Request");
    public static McpError MethodNotFound() => new(-32601, "Method not found");
    public static McpError InvalidParams() => new(-32602, "Invalid params");
    public static McpError InternalError() => new(-32603, "Internal error");
    public static McpError ServerNotInitialized() => new(-32002, "Server not initialized");
    public static McpError Custom(int code, string message) => new(code, message);
}