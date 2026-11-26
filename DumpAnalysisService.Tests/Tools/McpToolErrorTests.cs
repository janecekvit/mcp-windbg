using System.Net;
using System.Text;
using System.Text.Json;
using DumpAnalysisService.Tests.Security;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DumpAnalysisService.Tests.Tools;

/// <summary>What an MCP client and a REST caller see when a tool call or job fails for a reason they can act on.</summary>
[Collection(InProcessServiceCollection.Name)]
public sealed class McpToolErrorTests : IClassFixture<AuthenticationEndpointTests.JwtHost>
{
    private readonly AuthTestHost _host;

    public McpToolErrorTests(AuthenticationEndpointTests.JwtHost fixture) => _host = fixture.Host;

    private async Task<McpClient> _ClientAsync()
    {
        var http = _host.ClientWithToken(AuthTestHost.Token("errors-user", ["Dumps.Analyze"]));
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp"), TransportMode = HttpTransportMode.StreamableHttp },
            http, loggerFactory: null, ownsHttpClient: true);
        return await McpClient.CreateAsync(transport);
    }

    private async Task<string> _ErrorTextAsync(string tool, Dictionary<string, object?> arguments)
    {
        await using var client = await _ClientAsync();
        var result = await client.CallToolAsync(tool, arguments);
        Assert.True(result.IsError);
        return string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));
    }

    [Fact]
    public async Task ExecuteCommand_RefusedCommand_ReturnsThePolicyReason()
    {
        var text = await _ErrorTextAsync("execute_command", new() { ["session_id"] = "abcd1234", ["command"] = ".shell placeholder" });

        Assert.Contains(".shell", text);
        Assert.Contains("command policy", text);
    }

    [Fact]
    public async Task ExecuteCommand_UnknownSession_ReturnsNotFound()
    {
        var text = await _ErrorTextAsync("execute_command", new() { ["session_id"] = "abcd1234", ["command"] = "kb" });

        Assert.Contains("Session abcd1234 not found", text);
    }

    [Fact]
    public async Task LoadDump_MissingDump_ReturnsNotFound()
    {
        var text = await _ErrorTextAsync("load_dump", new() { ["dump_file_path"] = "placeholder-missing.dmp" });

        Assert.Contains("Dump file not found", text);
    }

    [Fact]
    public async Task CloseSession_BlankSessionId_ReturnsValidationMessage()
    {
        var text = await _ErrorTextAsync("close_session", new() { ["session_id"] = " " });

        Assert.Contains("Session ID is required", text);
    }

    [Fact]
    public async Task RestExecuteCommand_RefusedCommand_Returns400WithTheReason()
    {
        var http = _host.ClientWithToken(AuthTestHost.Token("errors-user", ["Dumps.Analyze"]));
        var response = await http.PostAsync("/api/jobs/execute-command",
            new StringContent(JsonSerializer.Serialize(new { sessionId = "abcd1234", command = ".shell placeholder" }), Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(".shell", await response.Content.ReadAsStringAsync());
    }
}
