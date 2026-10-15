using ModelContextProtocol.Client;

namespace DumpAnalysisService.Tests.Security;

/// <summary>The MCP endpoint over Streamable HTTP with bearer tokens: tools/list shows what the role allows.</summary>
public sealed class McpAuthorizationTests : IClassFixture<AuthenticationEndpointTests.JwtHost>
{
    private readonly AuthTestHost _host;

    public McpAuthorizationTests(AuthenticationEndpointTests.JwtHost fixture) => _host = fixture.Host;

    private async Task<IList<McpClientTool>> _ToolsAsync(string[] roles)
    {
        var http = _host.ClientWithToken(AuthTestHost.Token($"mcp-{string.Join('-', roles)}", roles));
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp"), TransportMode = HttpTransportMode.StreamableHttp },
            http, loggerFactory: null, ownsHttpClient: true);
        await using var client = await McpClient.CreateAsync(transport);
        return await client.ListToolsAsync();
    }

    private async Task<IList<string>> _ToolNamesAsync(string[] roles) =>
        (await _ToolsAsync(roles)).Select(tool => tool.Name).ToList();

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
    public async Task TheRequestContextParameter_IsNotPartOfAnyInputSchema()
    {
        foreach (var tool in await _ToolsAsync(["Dumps.Analyze"]))
            Assert.DoesNotContain("\"context\"", tool.JsonSchema.GetRawText());
    }
}
