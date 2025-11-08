using McpProxy.Services;
using McpProxy.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Serilog;
using Serilog.Events;
using Shared.Configuration;

namespace McpProxy;

internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        var useFileLogging = false;
        var envValue = Environment.GetEnvironmentVariable("USE_FILE_LOGGING");
        if (envValue != null)
            useFileLogging = Convert.ToBoolean(envValue);

        // Configure Serilog if file logging is enabled
        if (useFileLogging)
        {
            var logDirectory = ApplicationPaths.GetLogsDirectory();
            Directory.CreateDirectory(logDirectory);

            var logFilePath = Path.Combine(logDirectory, "mcpproxy-.log");

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .WriteTo.File(
                    logFilePath,
                    rollingInterval: RollingInterval.Day,
                    rollOnFileSizeLimit: true,
                    fileSizeLimitBytes: 10 * 1024 * 1024, // 10 MB
                    retainedFileCountLimit: 7,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
        }

        try
        {
            if (useFileLogging)
                Log.Information("McpProxy starting with file logging (using official MCP SDK)...");

            var hostBuilder = Host.CreateDefaultBuilder(args);

            // Configure services
            hostBuilder.ConfigureServices((context, services) =>
            {
                // Add HTTP client for BackgroundService API calls
                services.AddHttpClient();

                // Add SignalR client for progress notifications
                services.AddSingleton<ISignalRClientService, SignalRClientService>();

                // Add API service for BackgroundService communication
                services.AddScoped<IDebuggerApiService, DebuggerApiService>();

                // Add MCP SDK server with stdio transport and automatic tool discovery
                services.AddMcpServer()
                    .WithStdioServerTransport()
                    .WithToolsFromAssembly(); // Automatically discovers [McpServerTool] decorated methods
            });

            // Configure logging
            if (useFileLogging)
            {
                hostBuilder.UseSerilog();
            }
            else
            {
                hostBuilder.ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddConsole(options =>
                    {
                        // All logs go to stderr for MCP compatibility
                        options.LogToStandardErrorThreshold = LogLevel.Trace;
                    });
                    logging.SetMinimumLevel(LogLevel.Information);
                });
            }

            var host = hostBuilder.Build();

            // Connect to SignalR hub before starting MCP server
            var signalRClient = host.Services.GetRequiredService<ISignalRClientService>();
            await signalRClient.ConnectAsync();

            if (useFileLogging)
                Log.Information("Starting MCP server...");

            // MCP SDK automatically handles stdio communication, initialize/initialized, tools/list, tools/call
            await host.RunAsync();

            if (useFileLogging)
                Log.Information("McpProxy stopped gracefully");

            return 0;
        }
        catch (Exception ex)
        {
            if (useFileLogging)
                Log.Fatal(ex, "McpProxy terminated unexpectedly");
            else
                Console.Error.WriteLine($"Fatal error: {ex.Message}");

            return 1;
        }
        finally
        {
            if (useFileLogging)
                await Log.CloseAndFlushAsync();
        }
    }
}