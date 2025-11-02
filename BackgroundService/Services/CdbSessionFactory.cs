using BackgroundService.Infrastructure.Debugger;
using BackgroundService.Infrastructure.Detection;
using Shared.Configuration;

namespace BackgroundService.Services;

/// <summary>
/// Factory for creating CDB session instances with infrastructure dependencies injected.
/// Handles construction of CdbProcessManager and SymbolPathBuilder for each session.
/// </summary>
public sealed class CdbSessionFactory : ICdbSessionFactory
{
    private readonly ILogger<CdbSessionService> _sessionLogger;
    private readonly ILogger<CdbProcessManager> _processLogger;
    private readonly ILogger<SymbolPathBuilder> _symbolLogger;
    private readonly IAnalysisService _analysisService;
    private readonly IPathDetectionService _pathDetectionService;
    private readonly DebuggerConfiguration _configuration;
    private readonly string _cdbPath;

    public CdbSessionFactory(
        ILogger<CdbSessionService> sessionLogger,
        ILogger<CdbProcessManager> processLogger,
        ILogger<SymbolPathBuilder> symbolLogger,
        IAnalysisService analysisService,
        IPathDetectionService pathDetectionService,
        DebuggerConfiguration configuration,
        ILogger<CdbSessionFactory> factoryLogger)
    {
        _sessionLogger = sessionLogger;
        _processLogger = processLogger;
        _symbolLogger = symbolLogger;
        _analysisService = analysisService;
        _pathDetectionService = pathDetectionService;
        _configuration = configuration;

        // Detect or validate CDB path during factory construction
        if (!string.IsNullOrEmpty(_configuration.CdbPath) && _pathDetectionService.ValidateDebuggerPath(_configuration.CdbPath))
        {
            _cdbPath = _configuration.CdbPath;
            factoryLogger.LogInformation("Using CDB path from configuration: {Path}", _cdbPath);
        }
        else
        {
            _cdbPath = _pathDetectionService.GetBestDebuggerPath();
            factoryLogger.LogInformation("Auto-detected debugger path: {Path}", _cdbPath);
        }
    }

    public ICdbSessionService CreateSession(string sessionId)
    {
        // Create infrastructure components for this session
        var processManager = new CdbProcessManager(sessionId, _processLogger);

        var symbolPathBuilder = new SymbolPathBuilder(
            _configuration.SymbolCache,
            _configuration.SymbolPathExtra,
            _configuration.SymbolServers,
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
