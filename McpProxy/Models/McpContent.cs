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

    public static McpContent CreateText(string text) => new(text, "text");
}