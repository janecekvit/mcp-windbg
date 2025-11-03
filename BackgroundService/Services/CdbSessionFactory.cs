using BackgroundService.Infrastructure.Debugger;
using BackgroundService.Infrastructure.Detection;

namespace BackgroundService.Services;

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
    private readonly string _cdbPath;

    public CdbSessionFactory(
        ILogger<CdbSessionService> sessionLogger,
        ILogger<CdbProcessManager> processLogger,
        ILogger<SymbolPathBuilder> symbolLogger,
        IAnalysisService analysisService,
        IPathDetectionService pathDetectionService,
        ILogger<CdbSessionFactory> factoryLogger)
    {
        _sessionLogger = sessionLogger;
        _processLogger = processLogger;
        _symbolLogger = symbolLogger;
        _analysisService = analysisService;
        _pathDetectionService = pathDetectionService;

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
        Shared.Configuration.SymbolsConfiguration? symbols = null)
    {
        // Create infrastructure components for this session
        var processManager = new CdbProcessManager(sessionId, _processLogger);

        // Use symbol configuration from MCP server, or fall back to defaults
        var symbolCache = symbols?.SymbolCache ?? GetDefaultSymbolCache();
        var symbolPathExtra = symbols?.SymbolPathExtra ?? string.Empty;
        var symbolServers = symbols?.SymbolServers;

        var symbolPathBuilder = new SymbolPathBuilder(
            symbolCache,
            symbolPathExtra,
            symbolServers,
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

    private static string GetDefaultSymbolCache()
    {
        // Default: %LOCALAPPDATA%\CdbAnalysisServer\Symbols
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CdbAnalysisServer",
            "Symbols");
    }
}
