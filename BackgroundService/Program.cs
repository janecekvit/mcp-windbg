using BackgroundService.Factories;
using BackgroundService.Infrastructure.Detection;
using BackgroundService.Infrastructure.IO;
using BackgroundService.Providers;
using BackgroundService.Services;
using Shared;
using Shared.Extensions;

namespace BackgroundService;

internal class Program
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
                    policy.WithOrigins($"http://localhost:{Constants.Network.DefaultBackgroundServicePort}")
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

            // Start the service
            var port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : Constants.Network.DefaultBackgroundServicePort;
            app.Urls.Add($"http://localhost:{port}");

            logger.LogInformation("CDB Background Service listening on port {Port}", port);
            logger.LogInformation("MCP HTTP endpoint available at http://localhost:{Port}/mcp", port);
            logger.LogInformation("REST API available at http://localhost:{Port}/api", port);
            logger.LogInformation("SignalR hub available at http://localhost:{Port}/hubs/progress", port);

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