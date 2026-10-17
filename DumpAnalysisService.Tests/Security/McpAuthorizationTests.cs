using System.Net;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Shared.Models;

namespace DumpAnalysisService.Tests.Security;

/// <summary>The MCP endpoint over Streamable HTTP with bearer tokens: tools/list shows what the role allows.</summary>
[Collection(InProcessServiceCollection.Name)]
public sealed class McpAuthorizationTests : IClassFixture<AuthenticationEndpointTests.JwtHost>
{
    private readonly AuthTestHost _host;

    public McpAuthorizationTests(AuthenticationEndpointTests.JwtHost fixture) => _host = fixture.Host;

    private async Task<McpClient> _ClientAsync(string subject, string[] roles)
    {
        var http = _host.ClientWithToken(AuthTestHost.Token(subject, roles));
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp"), TransportMode = HttpTransportMode.StreamableHttp },
            http, loggerFactory: null, ownsHttpClient: true);
        return await McpClient.CreateAsync(transport);
    }

    private async Task<IList<McpClientTool>> _ToolsAsync(string[] roles)
    {
        await using var client = await _ClientAsync($"mcp-{string.Join('-', roles)}", roles);
        return await client.ListToolsAsync();
    }

    private async Task<IList<string>> _ToolNamesAsync(string[] roles) =>
        (await _ToolsAsync(roles)).Select(tool => tool.Name).ToList();

    private static async Task<string> _TextAsync(McpClient client, string tool)
    {
        var result = await client.CallToolAsync(tool, new Dictionary<string, object?>());
        Assert.NotEqual(true, result.IsError);
        return string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));
    }

    [Fact]
    public async Task Reader_SeesOnlyReadTools()
    {
        var tools = await _ToolNamesAsync(["Dumps.Read"]);

        Assert.Contains("list_jobs", tools);
        Assert.Contains("list_crash_groups", tools);
        Assert.DoesNotContain("load_dump", tools);
        Assert.DoesNotContain("execute_command", tools);
        Assert.DoesNotContain("triage_dump", tools);
    }

    [Fact]
    public async Task Analyst_SeesEveryTool()
    {
        var tools = await _ToolNamesAsync(["Dumps.Analyze"]);

        Assert.Equal(11, tools.Count);
        Assert.Contains("load_dump", tools);
        Assert.Contains("execute_command", tools);
    }

    [Fact]
    public async Task NoInputSchema_ExposesAnInternalParameter()
    {
        // The caller comes from a call-tool filter, not from a bound RequestContext parameter: a
        // parameter the SDK fails to bind would appear here and arrive empty.
        foreach (var tool in await _ToolsAsync(["Dumps.Analyze"]))
        {
            var schema = tool.JsonSchema.GetRawText();
            Assert.DoesNotContain("\"context\"", schema);
            Assert.DoesNotContain("\"progress\"", schema);
            Assert.DoesNotContain("\"cancellationToken\"", schema);
        }
    }

    [Fact]
    public async Task ToolCalls_RunAsTheTokensUser()
    {
        // A job alice starts over REST, then list_jobs over MCP as alice and as bob.
        var rest = _host.ClientWithToken(AuthTestHost.Token("mcp-alice", ["Dumps.Analyze"]));
        var created = await rest.PostAsync("/api/jobs/triage-dump",
            new StringContent(JsonSerializer.Serialize(new { dumpFilePath = @"C:\no\such.dmp" }), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);
        var jobId = (await JsonSerializer.DeserializeAsync<JobCreatedResponse>(
            await created.Content.ReadAsStreamAsync(), JsonSerializerOptions.Web))!.JobId;

        await using var alice = await _ClientAsync("mcp-alice", ["Dumps.Analyze"]);
        await using var bob = await _ClientAsync("mcp-bob", ["Dumps.Analyze"]);

        Assert.Contains(jobId, await _TextAsync(alice, "list_jobs"));
        Assert.DoesNotContain(jobId, await _TextAsync(bob, "list_jobs"));
    }
}
