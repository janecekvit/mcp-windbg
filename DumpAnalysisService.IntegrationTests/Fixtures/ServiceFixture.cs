using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace DumpAnalysisService.IntegrationTests.Fixtures;

public sealed class ServiceFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public HttpClient Client { get; private set; } = default!;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var symbolCache = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CdbMcpServer",
            "symbols");
        Directory.CreateDirectory(symbolCache);

        builder.ConfigureAppConfiguration(cfg =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Debugger:DefaultSymbolCache"] = symbolCache,
            });
        });

        builder.UseEnvironment("IntegrationTests");
    }

    public Task InitializeAsync()
    {
        Client = CreateClient();
        return Task.CompletedTask;
    }

    public new Task DisposeAsync()
    {
        Client?.Dispose();
        base.Dispose();
        return Task.CompletedTask;
    }
}
