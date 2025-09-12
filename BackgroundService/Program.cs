using BackgroundService.Services;
using Common;

namespace BackgroundService;

internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            var builder = WebApplication.CreateBuilder(args);

            // Configure services
            builder.Services.AddScoped<IPathDetectionService, PathDetectionService>();
            builder.Services.AddScoped<IAnalysisService, AnalysisService>();
            builder.Services.AddScoped<ISessionManagerService, SessionManagerService>();

            // Configure logging
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.SetMinimumLevel(LogLevel.Information);

            var app = builder.Build();
            var logger = app.Services.GetRequiredService<ILogger<Program>>();

            // Configure endpoints
            MapEndpoints(app, logger);

            // Start the service
            var port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : 8080;
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

    private static void MapEndpoints(WebApplication app, ILogger logger)
    {
        // Health check
        app.MapGet(ApiEndpoints.Health, () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

        // Load dump
        app.MapPost(ApiEndpoints.LoadDump, async (LoadDumpRequest request, ISessionManagerService sessionManager) =>
        {
            var sessionId = await sessionManager.CreateSessionWithDumpAsync(request.DumpFilePath);
            return Results.Ok(new LoadDumpResponse(sessionId, $"Session {sessionId} created successfully", request.DumpFilePath));
        });

        // Execute command
        app.MapPost(ApiEndpoints.ExecuteCommand, async (ExecuteCommandRequest request, ISessionManagerService sessionManager) =>
        {
            var result = await sessionManager.ExecuteCommandAsync(request.SessionId, request.Command);
            return Results.Ok(new CommandExecutionResponse(result));
        });

        // Basic analysis
        app.MapPost(ApiEndpoints.BasicAnalysis, async (BasicAnalysisRequest request, ISessionManagerService sessionManager) =>
        {
            var result = await sessionManager.ExecuteBasicAnalysisAsync(request.SessionId);
            return Results.Ok(new CommandExecutionResponse(result));
        });

        // Predefined analysis
        app.MapPost(ApiEndpoints.PredefinedAnalysis, async (PredefinedAnalysisRequest request, ISessionManagerService sessionManager) =>
        {
            var result = await sessionManager.ExecutePredefinedAnalysisAsync(request.SessionId, request.AnalysisType);
            return Results.Ok(new CommandExecutionResponse(result));
        });

        // List sessions
        app.MapGet(ApiEndpoints.Sessions, (ISessionManagerService sessionManager) =>
        {
            var sessions = sessionManager.GetActiveSessions()
                .Select(s => new SessionInfo(s.SessionId, s.DumpFile, s.IsActive))
                .ToList();
            return Results.Ok(new SessionsResponse(sessions));
        });

        // Close session
        app.MapDelete("/api/sessions/{sessionId}", (string sessionId, ISessionManagerService sessionManager) =>
        {
            sessionManager.CloseSession(sessionId);
            return Results.Ok(new CloseSessionResponse($"Session {sessionId} closed successfully"));
        });

        // Detect debuggers
        app.MapGet(ApiEndpoints.DetectDebuggers, (IPathDetectionService pathDetectionService) =>
        {
            var (cdbPath, winDbgPath, foundPaths) = pathDetectionService.DetectDebuggerPaths();
            
            var envVars = new Dictionary<string, string?>
            {
                ["CDB_PATH"] = Environment.GetEnvironmentVariable("CDB_PATH"),
                ["SYMBOL_CACHE"] = Environment.GetEnvironmentVariable("SYMBOL_CACHE"),
                ["SYMBOL_PATH_EXTRA"] = Environment.GetEnvironmentVariable("SYMBOL_PATH_EXTRA")
            };

            return Results.Ok(new DebuggerDetectionResponse(cdbPath, winDbgPath, foundPaths, envVars));
        });

        // List analyses
        app.MapGet(ApiEndpoints.Analyses, (IAnalysisService analysisService) =>
        {
            var analyses = analysisService.GetAvailableAnalyses()
                .Select(a => new AnalysisInfo(a, analysisService.GetAnalysisDescription(a)))
                .ToList();
            return Results.Ok(new AnalysesResponse(analyses));
        });
    }
}