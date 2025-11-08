using McpProxy.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
                Log.Information("McpProxy starting with file logging...");

            var hostBuilder = Host.CreateDefaultBuilder(args)
                .ConfigureServices((context, services) =>
                {
                    services.AddHttpClient();
                    services.AddScoped<IDebuggerApiService, DebuggerApiService>();
                    services.AddScoped<IToolsService, ToolsService>();
                    services.AddScoped<ICommunicationService, CommunicationService>();
                    services.AddSingleton<ISignalRClientService, SignalRClientService>();
                    services.AddSingleton<McpProxy>();
                });

            // Configure logging based on appsettings
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
                        options.LogToStandardErrorThreshold = LogLevel.Trace;
                    });
                    logging.SetMinimumLevel(LogLevel.Information);
                });
            }

            var host = hostBuilder.Build();

            // Connect to SignalR hub
            var signalRClient = host.Services.GetRequiredService<ISignalRClientService>();
            await signalRClient.ConnectAsync();

            var mcpServerProxy = host.Services.GetRequiredService<McpProxy>();
            await mcpServerProxy.RunAsync();

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