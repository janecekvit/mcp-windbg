using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using McpProxy.Services;

namespace McpProxy;

class Program
{
    static async Task<int> Main(string[] args)
    {
        try
        {
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureServices((context, services) =>
                {
                    services.AddHttpClient();
                    services.AddScoped<IDebuggerApiService, DebuggerApiService>();
                    services.AddScoped<IMcpInitializationService, McpInitializationService>();
                    services.AddScoped<IMcpToolsService, McpToolsService>();
                    services.AddScoped<IMcpCommunicationService, McpCommunicationService>();
                    services.AddSingleton<McpProxy>();
                })
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddConsole(options =>
                    {
                        options.LogToStandardErrorThreshold = LogLevel.Trace;
                    });
                    logging.SetMinimumLevel(LogLevel.Information);
                })
                .Build();

            var mcpServerProxy = host.Services.GetRequiredService<McpProxy>();
            await mcpServerProxy.RunAsync();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            return 1;
        }
    }
}