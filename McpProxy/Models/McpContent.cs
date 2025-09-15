using System.Text.Json.Serialization;

namespace McpProxy.Models;

public class McpContent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    public McpContent()
    {
    }

    public McpContent(string text, string type = "text")
    {
        Text = text;
        Type = type;
    }
}

public static class McpContentExtensions
{
    /// <summary>
    /// Creates an MCP text content object from a string
    /// </summary>
    /// <param name="text">The text content</param>
    /// <returns>MCP content object with type "text"</returns>
    public static McpContent ToMcpContent(this string text) => new(text, "text");
}