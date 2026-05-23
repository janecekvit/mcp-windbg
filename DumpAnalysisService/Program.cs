using DumpAnalysisService.Factories;
using DumpAnalysisService.Infrastructure.Detection;
using DumpAnalysisService.Infrastructure.IO;
using DumpAnalysisService.Providers;
using DumpAnalysisService.Services;
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

            // Infrastructure services
            builder.Services.AddSingleton<IPathExpansionService, PathExpansionService>();
            builder.Services.AddSingleton<IPathDetectionService, PathDetectionService>();

            // Application services
            builder.Services.AddSingleton<IAnalysisService, AnalysisService>();
            builder.Services.AddSingleton<IDiagnosticsService, DiagnosticsService>();
            builder.Services.AddSingleton<ICdbSessionFactory, CdbSessionFactory>();
            builder.Services.AddSingleton<ISessionManagerService, SessionManagerService>();
            builder.Services.AddSingleton<IJobManagerService, JobManagerService>();

            // MCP task store backed by JobManager (experimental MCP Tasks API)
#pragma warning disable MCPEXP001
            builder.Services.AddSingleton<ModelContextProtocol.IMcpTaskStore, Tasks.JobManagerBackedTaskStore>();
#pragma warning restore MCPEXP001

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
                    options.IdleTimeout = TimeSpan.FromHours(1);
                    options.Stateless = false; // Enable session state for progress notifications
                })
                .WithToolsFromAssembly();

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

}