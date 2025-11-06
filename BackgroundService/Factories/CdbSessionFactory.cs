using BackgroundService.Infrastructure.Debugger;
using BackgroundService.Infrastructure.Detection;
using BackgroundService.Services;
using Shared.Configuration;

namespace BackgroundService.Factories;

/// <summary>
/// Factory for creating CDB session instances with infrastructure dependencies injected.
/// Handles construction of CdbProcessManager and SymbolPathBuilder for each session.
/// Symbol configuration is received per-request from MCP server.
/// </summary>
public sealed class CdbSessionFactory : ICdbSessionFactory
{
    private readonly ILogger<CdbSessionService> _sessionLogger;
    private readonly ILogger<CdbProcessManager> _processLogger;
    private readonly ILogger<SymbolPathBuilder> _symbolLogger;
    private readonly IAnalysisService _analysisService;
    private readonly IPathDetectionService _pathDetectionService;
    private readonly DebuggerConfiguration _debuggerConfig;
    private readonly string _cdbPath;

    public CdbSessionFactory(
        ILogger<CdbSessionService> sessionLogger,
        ILogger<CdbProcessManager> processLogger,
        ILogger<SymbolPathBuilder> symbolLogger,
        IAnalysisService analysisService,
        IPathDetectionService pathDetectionService,
        DebuggerConfiguration debuggerConfig,
        ILogger<CdbSessionFactory> factoryLogger)
    {
        _sessionLogger = sessionLogger;
        _processLogger = processLogger;
        _symbolLogger = symbolLogger;
        _analysisService = analysisService;
        _pathDetectionService = pathDetectionService;
        _debuggerConfig = debuggerConfig;

        // Detect or validate CDB path during factory construction
        var path = _pathDetectionService.GetBestDebuggerPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            factoryLogger.LogError("No valid CDB installation found on the system.");
            throw new InvalidOperationException("CDB debugger not found. Please ensure it is installed.");
        }

        _cdbPath = path;
        factoryLogger.LogInformation("Auto-detected debugger path: {Path}", path);
    }

    public ICdbSessionService CreateSession(
        string sessionId,
        SymbolsConfiguration? symbols = null)
    {
        // Create infrastructure components for this session
        var processManager = new CdbProcessManager(sessionId, _processLogger);

        // Use symbol configuration from client, or fall back to defaults from configuration
        var symbolCache = symbols?.SymbolCache ?? _debuggerConfig.GetSymbolCachePath();
        var symbolPathExtra = symbols?.SymbolPathExtra ?? _debuggerConfig.DefaultSymbolPathExtra;
        var symbolServers = symbols?.SymbolServers;

        var symbolPathBuilder = new SymbolPathBuilder(
            symbolCache,
            symbolPathExtra,
            symbolServers,
            _debuggerConfig,
            _symbolLogger);

        // Create and return session service with infrastructure dependencies
        return new CdbSessionService(
            sessionId,
            _sessionLogger,
            _analysisService,
            processManager,
            symbolPathBuilder,
            _cdbPath);
    }
}
