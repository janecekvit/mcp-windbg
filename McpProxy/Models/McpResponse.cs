using System.Text.Json.Serialization;

namespace McpProxy.Models;

public class McpResponse
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("result")]
    public object? Result { get; set; }

    [JsonPropertyName("error")]
    public McpError? Error { get; set; }

    public McpResponse()
    {
    }

    public McpResponse(int id, object? result = null, McpError? error = null)
    {
        Id = id;
        Result = result;
        Error = error;
    }

    // Factory methods for success responses
    public static McpResponse Success(int id, object result) => new(id, result);
    
    public static McpResponse Success(int id) => new(id, new object());

    // Factory methods for error responses
    public static McpResponse CreateError(int id, McpError error) => new(id, error: error);
    
    public static McpResponse CreateError(int id, int code, string message) => new(id, error: new McpError(code, message));

    // Convenience methods for common errors
    public static McpResponse NotInitialized(int id) => CreateError(id, McpError.ServerNotInitialized());
    
    public static McpResponse MethodNotFound(int id, string method) => CreateError(id, McpError.Custom(-32601, $"Method not found: {method}"));
    
    public static McpResponse InvalidParams(int id) => CreateError(id, McpError.InvalidParams());
    
    public static McpResponse InternalError(int id, string? message = null) => 
        CreateError(id, McpError.Custom(-32603, message ?? "Internal error"));
}