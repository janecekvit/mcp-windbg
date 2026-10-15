using DumpAnalysisService.Factories;
using DumpAnalysisService.Infrastructure.Detection;
using DumpAnalysisService.Infrastructure.IO;
using DumpAnalysisService.Providers;
using DumpAnalysisService.Security;
using DumpAnalysisService.Services;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Extensions.Tasks;
using Shared;
using Shared.Extensions;

namespace DumpAnalysisService;

public partial class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            var builder = WebApplication.CreateBuilder(args);

            // Configuration
            var debuggerConfig = builder.Configuration.GetDebuggerConfiguration();
            builder.Services.AddSingleton(debuggerConfig);
            builder.Services.Configure<SecurityOptions>(builder.Configuration.GetSection(SecurityOptions.SectionName));

            // Infrastructure services
            builder.Services.AddSingleton<IPathExpansionService, PathExpansionService>();
            builder.Services.AddSingleton<IPathDetectionService, PathDetectionService>();

            // Application services
            builder.Services.AddSingleton<IAnalysisService, AnalysisService>();
            builder.Services.AddSingleton<IDiagnosticsService, DiagnosticsService>();
            builder.Services.AddSingleton<ICdbSessionFactory, CdbSessionFactory>();
            builder.Services.AddSingleton<ISessionManagerService, SessionManagerService>();
            builder.Services.AddSingleton<IJobManagerService, JobManagerService>();
            builder.Services.AddSingleton<IDumpTriageService, DumpTriageService>();

            // HTTP context accessor for reading request headers
            builder.Services.AddHttpContextAccessor();

            // Symbol configuration provider (scoped per HTTP request)
            builder.Services.AddScoped<ISymbolConfigurationProvider, HttpHeaderSymbolConfigurationProvider>();

            // Configure SignalR for real-time progress notifications
            builder.Services.AddSignalR();

            // Configure MCP Server with HTTP transport
            builder.Services.AddMcpServer()
                .WithHttpTransport(options =>
                {
                    // Clients on protocol 2025-11-25 and older (initialize handshake) keep a
                    // stateful session, as with SDK 1.x; clients on 2026-07-28+ are served
                    // statelessly on the same endpoint (SEP-2567).
                    options.SessionMode = HttpServerSessionMode.StatefulForInitializeClients;
                })
                .WithToolsFromAssembly()
                // MCP Tasks extension (SEP-2663): a task-aware client can run any tool call
                // as a task and poll tasks/get instead of holding the request open.
                // Tool calls still create REST-visible jobs in JobManagerService.
                .WithTasks(new InMemoryMcpTaskStore());

            // Configure controllers
            builder.Services.AddControllers();

            // Configure Problem Details for standardized error responses
            builder.Services.AddProblemDetails();

            // Configure CORS for SignalR
            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    policy.WithOrigins($"http://localhost:{Constants.Network.DefaultPort}")
                          .AllowAnyHeader()
                          .AllowAnyMethod()
                          .AllowCredentials();
                });
            });

            // Configure logging
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.SetMinimumLevel(LogLevel.Information);

            var app = builder.Build();
            var logger = app.Services.GetRequiredService<ILogger<Program>>();

            // Configure error handling middleware
            app.UseExceptionHandler();
            app.UseStatusCodePages();

            // Enable CORS
            app.UseCors();

            // Shared-secret authentication for REST, MCP and SignalR (no-op without Security:ApiKey)
            app.UseMiddleware<ApiKeyMiddleware>();

            // Configure controllers
            app.MapControllers();

            // Map MCP HTTP endpoints (/mcp/sse and /mcp/messages)
            app.MapMcp("/mcp");

            // Map SignalR hub
            app.MapHub<Hubs.ProgressHub>("/hubs/progress");

            // Start the service.
            // Docker/Azure set ASPNETCORE_URLS (e.g. http://+:7997) and WebApplication
            // binds from it natively. Programmatic app.Urls.Add OVERRIDES ASPNETCORE_URLS,
            // so only add an explicit loopback URL for local runs where the env var is
            // absent. To bind elsewhere (different port or interface), set ASPNETCORE_URLS.
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
                app.Urls.Add($"http://localhost:{Constants.Network.DefaultPort}");

            app.Lifetime.ApplicationStarted.Register(() =>
            {
                _WarnIfExposedWithoutApiKey(app, logger);

                foreach (var boundUrl in app.Urls)
                {
                    logger.LogInformation("Dump Analysis Service listening on {Url}", boundUrl);
                    logger.LogInformation("MCP HTTP endpoint available at {Url}/mcp", boundUrl);
                    logger.LogInformation("REST API available at {Url}/api", boundUrl);
                    logger.LogInformation("SignalR hub available at {Url}/hubs/progress", boundUrl);
                }
            });

            await app.RunAsync();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Logs a warning when the service listens on a non-loopback address without an API key:
    /// anyone who can reach the port can then load dumps and run debugger commands.
    /// </summary>
    private static void _WarnIfExposedWithoutApiKey(WebApplication app, ILogger logger)
    {
        var security = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<SecurityOptions>>().Value;
        if (!string.IsNullOrWhiteSpace(security.ApiKey))
            return;

        foreach (var url in app.Urls)
        {
            if (!Uri.TryCreate(url.Replace("://+", "://0.0.0.0").Replace("://*", "://0.0.0.0"), UriKind.Absolute, out var uri)
                || !uri.IsLoopback)
            {
                logger.LogWarning(
                    "Listening on {Url} without Security:ApiKey - every client that can reach this port can use the debugger. " +
                    "Set Security__ApiKey (and send it as X-Api-Key) when the service is reachable from other machines.", url);
            }
        }
    }
}
