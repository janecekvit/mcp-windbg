using System.Text.Json.Serialization;

namespace McpProxy.Models;

public class McpToolResult
{
    [JsonPropertyName("content")]
    public IReadOnlyList<McpContent> Content { get; set; } = Array.Empty<McpContent>();

    [JsonPropertyName("isError")]
    public bool IsError { get; set; }

    public McpToolResult()
    {
    }

    public McpToolResult(IReadOnlyList<McpContent> content, bool isError = false)
    {
        Content = content;
        IsError = isError;
    }

    public McpToolResult(string text, bool isError = false)
    {
        Content = new[] { text.ToMcpContent() };
        IsError = isError;
    }

    public static McpToolResult Success(string text) => new(text, isError: false);
    
    public static McpToolResult Success(IReadOnlyList<McpContent> content) => new(content, isError: false);
    
    public static McpToolResult Error(string errorMessage) => new(errorMessage, isError: true);
    
    public static McpToolResult Error(IReadOnlyList<McpContent> content) => new(content, isError: true);
    
    public static McpToolResult Error(Exception ex, string? prefix = null)
    {
        var message = string.IsNullOrEmpty(prefix) ? ex.Message : $"{prefix}: {ex.Message}";
        return new(message, isError: true);
    }
}