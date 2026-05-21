using System.Net;
using DumpAnalysisService.IntegrationTests.Fixtures;

namespace DumpAnalysisService.IntegrationTests.Tests;

[Collection(IntegrationCollection.Name)]
[Trait("Category", "Integration")]
public class SmokeTests
{
    private readonly ServiceFixture _service;

    public SmokeTests(ServiceFixture service)
    {
        _service = service;
    }

    [Fact]
    public async Task JobsEndpoint_OnEmptyService_ReturnsOk()
    {
        var response = await _service.Client.GetAsync("/api/jobs");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(body));
    }

    [Fact]
    public async Task DiagnosticsAnalysesEndpoint_ListsAllAnalysisTypes()
    {
        var response = await _service.Client.GetAsync("/api/diagnostics/analyses");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("basic", body);
        Assert.Contains("threads", body);
        Assert.Contains("heap", body);
    }
}
