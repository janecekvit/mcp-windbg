using BackgroundService.Services;
using Shared;

namespace BackgroundService;

internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            var builder = WebApplication.CreateBuilder(args);

            // Configure services
            builder.Services.AddSingleton<IPathDetectionService, PathDetectionService>();
            builder.Services.AddSingleton<IAnalysisService, AnalysisService>();
            builder.Services.AddSingleton<ISessionManagerService, SessionManagerService>();

            // Configure controllers
            builder.Services.AddControllers();

            // Configure Problem Details for standardized error responses
            builder.Services.AddProblemDetails();

            // Configure logging
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.SetMinimumLevel(LogLevel.Information);

            var app = builder.Build();
            var logger = app.Services.GetRequiredService<ILogger<Program>>();

            // Configure error handling middleware
            app.UseExceptionHandler();
            app.UseStatusCodePages();

            // Configure controllers
            app.MapControllers();

            // Start the service
            var port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : Constants.Network.DefaultBackgroundServicePort;
            app.Urls.Add($"http://localhost:{port}");

            logger.LogInformation("CDB Background Service listening on port {Port}", port);

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