using System.Net.Http.Json;
using DumpAnalysisService.Security;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Shared.Models;

namespace DumpAnalysisService.Tests.Security;

/// <summary>Without any credential configured, each stateful MCP client owns the jobs and sessions it creates.</summary>
[Collection(InProcessServiceCollection.Name)]
public sealed class McpSessionOwnershipTests : IClassFixture<McpSessionOwnershipTests.NoAuthHost>
{
    public sealed class NoAuthHost : IDisposable
    {
        public AuthTestHost Host { get; } = new(jwt: false, apiKey: null);
        public void Dispose() => Host.Dispose();
    }

    private readonly AuthTestHost _host;

    public McpSessionOwnershipTests(NoAuthHost fixture) => _host = fixture.Host;

    // 2025-11-25 and older clients get a stateful MCP session (Mcp-Session-Id); newer ones are stateless.
    private async Task<McpClient> _ClientAsync(string protocolVersion = "2025-11-25")
    {
        var http = _host.CreateClient();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp"), TransportMode = HttpTransportMode.StreamableHttp },
            http, loggerFactory: null, ownsHttpClient: true);
        return await McpClient.CreateAsync(transport, new McpClientOptions { ProtocolVersion = protocolVersion });
    }

    private static async Task<string> _TextAsync(McpClient client, string tool, Dictionary<string, object?>? arguments = null)
    {
        var result = await client.CallToolAsync(tool, arguments ?? []);
        return string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));
    }

    [Fact]
    public async Task JobsOfOneMcpSession_AreInvisibleToAnother_ButNotToRest()
    {
        await using var alice = await _ClientAsync();
        await using var bob = await _ClientAsync();
        Assert.NotEqual(alice.SessionId, bob.SessionId);

        // A job alice's MCP session creates (the dump does not exist, so it fails, but it exists).
        await _TextAsync(alice, "triage_dump", new() { ["dump_file_path"] = "placeholder-missing.dmp" });

        var jobs = await _host.CreateClient().GetFromJsonAsync<List<JobStatus>>("/api/jobs");
        var jobId = jobs!.Where(j => j.Operation == JobOperationType.TriageDump).OrderBy(j => j.CreatedAt).Last().JobId;

        Assert.Contains(jobId, await _TextAsync(alice, "list_jobs"));
        Assert.DoesNotContain(jobId, await _TextAsync(bob, "list_jobs"));
    }

    [Fact]
    public async Task StatelessClients_KeepSeeingEveryJob()
    {
        await using var stateful = await _ClientAsync();
        await _TextAsync(stateful, "triage_dump", new() { ["dump_file_path"] = "placeholder-other.dmp" });
        var jobs = await _host.CreateClient().GetFromJsonAsync<List<JobStatus>>("/api/jobs");
        var jobId = jobs!.Where(j => j.Operation == JobOperationType.TriageDump).OrderBy(j => j.CreatedAt).Last().JobId;

        await using var stateless = await _ClientAsync(protocolVersion: "2026-07-28");
        Assert.Null(stateless.SessionId);
        Assert.Contains(jobId, await _TextAsync(stateless, "list_jobs"));
    }

    [Fact]
    public void ServiceRunsWithoutAuthentication()
    {
        var options = _host.Services.GetService(typeof(Microsoft.Extensions.Options.IOptions<SecurityOptions>)) as Microsoft.Extensions.Options.IOptions<SecurityOptions>;
        Assert.False(options!.Value.AuthenticationRequired);
    }
}
