using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using System.Text;

namespace CdbBackgroundService;

class Program
{
    static async Task<int> Main(string[] args)
    {
        try
        {
            var builder = WebApplication.CreateBuilder(args);
            
            // Configure services
            builder.Services.AddSingleton<CdbSessionManager>();
            
            // Configure logging
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.SetMinimumLevel(LogLevel.Information);
            
            // Build the app
            var app = builder.Build();
            
            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            var sessionManager = app.Services.GetRequiredService<CdbSessionManager>();
            
            logger.LogInformation("Starting CDB Background Service...");
            
            // Configure HTTP endpoints
            ConfigureEndpoints(app, sessionManager, logger);
            
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
    
    private static void ConfigureEndpoints(WebApplication app, CdbSessionManager sessionManager, ILogger logger)
    {
        // Health check
        app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));
        
        // Load dump and create session
        app.MapPost("/api/load-dump", async (LoadDumpRequest request) =>
        {
            try
            {
                logger.LogInformation("Loading dump: {DumpFile}", request.DumpFilePath);
                var (success, sessionId, message) = await sessionManager.CreateSessionWithDumpAsync(request.DumpFilePath);
                
                return success 
                    ? Results.Ok(new { sessionId, message, dumpFile = request.DumpFilePath })
                    : Results.BadRequest(new { error = message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error loading dump: {DumpFile}", request.DumpFilePath);
                return Results.StatusCode(500);
            }
        });
        
        // Execute command
        app.MapPost("/api/execute-command", async (ExecuteCommandRequest request) =>
        {
            try
            {
                logger.LogInformation("Executing command in session {SessionId}: {Command}", request.SessionId, request.Command);
                var (success, result) = await sessionManager.ExecuteCommandAsync(request.SessionId, request.Command);
                
                return success 
                    ? Results.Ok(new { result })
                    : Results.BadRequest(new { error = result });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error executing command in session {SessionId}", request.SessionId);
                return Results.StatusCode(500);
            }
        });
        
        // Basic analysis
        app.MapPost("/api/basic-analysis", async (BasicAnalysisRequest request) =>
        {
            try
            {
                logger.LogInformation("Running basic analysis for session {SessionId}", request.SessionId);
                var (success, result) = await sessionManager.ExecuteBasicAnalysisAsync(request.SessionId);
                
                return success 
                    ? Results.Ok(new { result })
                    : Results.BadRequest(new { error = result });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error running basic analysis for session {SessionId}", request.SessionId);
                return Results.StatusCode(500);
            }
        });
        
        // Predefined analysis
        app.MapPost("/api/predefined-analysis", async (PredefinedAnalysisRequest request) =>
        {
            try
            {
                logger.LogInformation("Running {AnalysisType} analysis for session {SessionId}", request.AnalysisType, request.SessionId);
                var (success, result) = await sessionManager.ExecutePredefinedAnalysisAsync(request.SessionId, request.AnalysisType);
                
                return success 
                    ? Results.Ok(new { result })
                    : Results.BadRequest(new { error = result });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error running predefined analysis for session {SessionId}", request.SessionId);
                return Results.StatusCode(500);
            }
        });
        
        // List sessions
        app.MapGet("/api/sessions", () =>
        {
            try
            {
                var sessions = sessionManager.GetActiveSessions();
                return Results.Ok(new { sessions });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error listing sessions");
                return Results.StatusCode(500);
            }
        });
        
        // Close session
        app.MapDelete("/api/sessions/{sessionId}", (string sessionId) =>
        {
            try
            {
                logger.LogInformation("Closing session {SessionId}", sessionId);
                var (success, message) = sessionManager.CloseSession(sessionId);
                
                return success 
                    ? Results.Ok(new { message })
                    : Results.BadRequest(new { error = message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error closing session {SessionId}", sessionId);
                return Results.StatusCode(500);
            }
        });
        
        // Detect debuggers
        app.MapGet("/api/detect-debuggers", () =>
        {
            try
            {
                var (cdbPath, winDbgPath, foundPaths) = CdbPathDetector.DetectDebuggerPaths(logger);
                
                return Results.Ok(new 
                { 
                    cdbPath, 
                    winDbgPath, 
                    foundPaths,
                    environmentVariables = new
                    {
                        CDB_PATH = Environment.GetEnvironmentVariable("CDB_PATH"),
                        SYMBOL_CACHE = Environment.GetEnvironmentVariable("SYMBOL_CACHE"),
                        SYMBOL_PATH_EXTRA = Environment.GetEnvironmentVariable("SYMBOL_PATH_EXTRA")
                    }
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error detecting debuggers");
                return Results.StatusCode(500);
            }
        });
        
        // List available analyses
        app.MapGet("/api/analyses", () =>
        {
            try
            {
                var analyses = PredefinedAnalyses.GetAvailableAnalyses()
                    .Select(a => new { name = a, description = PredefinedAnalyses.GetAnalysisDescription(a) })
                    .ToArray();
                    
                return Results.Ok(new { analyses });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error listing analyses");
                return Results.StatusCode(500);
            }
        });
    }
}

// Request DTOs
public record LoadDumpRequest(string DumpFilePath);
public record ExecuteCommandRequest(string SessionId, string Command);
public record BasicAnalysisRequest(string SessionId);
public record PredefinedAnalysisRequest(string SessionId, string AnalysisType);