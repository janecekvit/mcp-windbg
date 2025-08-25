using System.Text.Json.Serialization;

namespace McpProxy.Models;

public class McpToolResult
{
    [JsonPropertyName("content")]
    public McpContent[] Content { get; set; } = Array.Empty<McpContent>();

    [JsonPropertyName("isError")]
    public bool IsError { get; set; }
}