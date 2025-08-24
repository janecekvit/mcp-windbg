using System.Text.Json.Serialization;

namespace CdbMcpServer.Models;

public class McpError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}