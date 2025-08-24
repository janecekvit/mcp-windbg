using System.Text.Json.Serialization;

namespace CdbMcpServer.Models;

public class McpToolResult
{
    [JsonPropertyName("content")]
    public McpContent[] Content { get; set; } = Array.Empty<McpContent>();
    
    [JsonPropertyName("isError")]
    public bool IsError { get; set; }
}