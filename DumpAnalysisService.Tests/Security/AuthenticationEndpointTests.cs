using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shared.Models;

namespace DumpAnalysisService.Tests.Security;

[Collection(InProcessServiceCollection.Name)]
public sealed class AuthenticationEndpointTests : IClassFixture<AuthenticationEndpointTests.JwtHost>
{
    public sealed class JwtHost : IDisposable
    {
        public AuthTestHost Host { get; } = new();
        public void Dispose() => Host.Dispose();
    }

    private readonly AuthTestHost _host;

    public AuthenticationEndpointTests(JwtHost fixture) => _host = fixture.Host;

    private static readonly string[] Reader = ["Dumps.Read"];
    private static readonly string[] Analyst = ["Dumps.Analyze"];
    private static readonly string[] Admin = ["Dumps.Admin"];

    private static StringContent _Json(object body) =>
        new(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json");

    private static async Task<string> _StartTriageJobAsync(HttpClient client)
    {
        var response = await client.PostAsync("/api/jobs/triage-dump", _Json(new { dumpFilePath = @"C:\no\such.dmp" }));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JobCreatedResponse>(JsonSerializerOptions.Web);
        return created!.JobId;
    }

    [Fact]
    public async Task Health_IsAnonymous()
    {
        var response = await _host.CreateClient().GetAsync("/api/diagnostics/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task NoCredential_Is401_AndPointsAtTheResourceMetadata()
    {
        var response = await _host.CreateClient().PostAsync("/mcp", _Json(new { jsonrpc = "2.0", id = 1, method = "tools/list" }));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenge = string.Join(" ", response.Headers.WwwAuthenticate.Select(h => h.ToString()));
        Assert.Contains("Bearer", challenge);
        Assert.Contains("resource_metadata=", challenge);
        Assert.Contains("/.well-known/oauth-protected-resource", challenge);
    }

    [Fact]
    public async Task ResourceMetadata_IsAnonymous_AndNamesTheAuthority()
    {
        var response = await _host.CreateClient().GetAsync("/.well-known/oauth-protected-resource/mcp");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(AuthTestHost.Issuer, document.RootElement.GetProperty("authorization_servers")[0].GetString());
        Assert.EndsWith("/mcp", document.RootElement.GetProperty("resource").GetString());
        Assert.Equal($"{AuthTestHost.Audience}/Dumps.Analyze", document.RootElement.GetProperty("scopes_supported")[0].GetString());
    }

    [Fact]
    public async Task ReaderToken_ListsJobs_ButCannotTriage()
    {
        var client = _host.ClientWithToken(AuthTestHost.Token("reader", Reader));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/jobs")).StatusCode);
        var triage = await client.PostAsync("/api/jobs/triage-dump", _Json(new { dumpFilePath = @"C:\no\such.dmp" }));
        Assert.Equal(HttpStatusCode.Forbidden, triage.StatusCode);
    }

    [Fact]
    public async Task TokenWithoutRole_Is403()
    {
        var client = _host.ClientWithToken(AuthTestHost.Token("nobody"));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/jobs")).StatusCode);
    }

    [Fact]
    public async Task RoleInTheScopeClaim_IsNotAcceptedByDefault()
    {
        var client = _host.ClientWithToken(AuthTestHost.Token("delegated", scope: "openid Dumps.Read"));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/crash-groups")).StatusCode);
    }

    [Theory]
    [InlineData("audience")]
    [InlineData("issuer")]
    [InlineData("expired")]
    public async Task InvalidToken_Is401(string defect)
    {
        var token = defect switch
        {
            "audience" => AuthTestHost.Token("analyst", Analyst, audience: "api://someone-else"),
            "issuer" => AuthTestHost.Token("analyst", Analyst, issuer: "https://evil.test/v2.0"),
            _ => AuthTestHost.Token("analyst", Analyst, expires: DateTime.UtcNow.AddMinutes(-10)),
        };

        var response = await _host.ClientWithToken(token).GetAsync("/api/jobs");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Jobs_BelongToTheirCreator()
    {
        var alice = _host.ClientWithToken(AuthTestHost.Token("alice", Analyst));
        var bob = _host.ClientWithToken(AuthTestHost.Token("bob", Analyst));
        var admin = _host.ClientWithToken(AuthTestHost.Token("root", Admin));

        var jobId = await _StartTriageJobAsync(alice);

        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync($"/api/jobs/{jobId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/jobs/{jobId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsync($"/api/jobs/{jobId}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/jobs/{jobId}")).StatusCode);

        var bobsJobs = await bob.GetFromJsonAsync<List<JobStatus>>("/api/jobs", JsonSerializerOptions.Web);
        Assert.DoesNotContain(bobsJobs!, job => job.JobId == jobId);
        var adminJobs = await admin.GetFromJsonAsync<List<JobStatus>>("/api/jobs", JsonSerializerOptions.Web);
        Assert.Contains(adminJobs!, job => job.JobId == jobId);
    }

    [Fact]
    public async Task ApiKey_StillWorks_AlongsideTokens_AndSeesEveryJob()
    {
        var alice = _host.ClientWithToken(AuthTestHost.Token("alice", Analyst));
        var jobId = await _StartTriageJobAsync(alice);

        var apiKey = _host.ClientWithApiKey();
        Assert.Equal(HttpStatusCode.OK, (await apiKey.GetAsync($"/api/jobs/{jobId}")).StatusCode);

        // A job started with the API key is unowned: a non-admin token cannot see it.
        var unowned = await _StartTriageJobAsync(apiKey);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/jobs/{unowned}")).StatusCode);
    }

    [Fact]
    public async Task WrongApiKey_Is401()
    {
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "wrong");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/jobs")).StatusCode);
    }
}

[Collection(InProcessServiceCollection.Name)]
public sealed class ApiKeyOnlyEndpointTests : IDisposable
{
    private readonly AuthTestHost _host = new(jwt: false);

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task MissingKey_Is401_WithoutResourceMetadata()
    {
        var response = await _host.CreateClient().GetAsync("/api/jobs");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("resource_metadata", string.Join(" ", response.Headers.WwwAuthenticate.Select(h => h.ToString())));
    }

    [Theory]
    [InlineData("header")]
    [InlineData("bearer")]
    public async Task Key_IsAccepted(string how)
    {
        var client = _host.CreateClient();
        if (how == "header")
            client.DefaultRequestHeaders.Add("X-Api-Key", AuthTestHost.ApiKey);
        else
            client.DefaultRequestHeaders.Authorization = new("Bearer", AuthTestHost.ApiKey);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/jobs")).StatusCode);
    }

    [Fact]
    public async Task Health_IsAnonymous()
    {
        Assert.Equal(HttpStatusCode.OK, (await _host.CreateClient().GetAsync("/api/diagnostics/health")).StatusCode);
    }

    [Fact]
    public async Task ResourceMetadata_IsNotServed()
    {
        var response = await _host.CreateClient().GetAsync("/.well-known/oauth-protected-resource/mcp");
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }
}

[Collection(InProcessServiceCollection.Name)]
public sealed class NoAuthenticationEndpointTests : IDisposable
{
    private readonly AuthTestHost _host = new(jwt: false, apiKey: null);

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task EverythingIsOpen_AsBefore()
    {
        var client = _host.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/jobs")).StatusCode);
        var triage = await client.PostAsync("/api/jobs/triage-dump",
            new StringContent("""{"dumpFilePath":"C:\\no\\such.dmp"}""", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Accepted, triage.StatusCode);
    }
}
