using System.Collections.Concurrent;
using BackgroundService.Models;

namespace BackgroundService.Services;

public sealed class SessionManagerService : ISessionManagerService
{
    private readonly ILogger<SessionManagerService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IPathDetectionService _pathDetectionService;
    private readonly IAnalysisService _analysisService;
    private readonly ConcurrentDictionary<string, ICdbSessionService> _sessions = new();
    private readonly string _cdbPath;
    private readonly string _symbolCache;
    private readonly string _symbolPathExtra;

    public SessionManagerService(ILogger<SessionManagerService> logger,
                               ILoggerFactory loggerFactory,
                               IPathDetectionService pathDetectionService,
                               IAnalysisService analysisService)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _pathDetectionService = pathDetectionService;
        _analysisService = analysisService;

        // Auto-detect CDB path or use environment variable
        var envCdbPath = Environment.GetEnvironmentVariable("CDB_PATH");
        if (!string.IsNullOrEmpty(envCdbPath) && _pathDetectionService.ValidateDebuggerPath(envCdbPath))
        {
            _cdbPath = envCdbPath;
            _logger.LogInformation("Using CDB path from environment variable: {Path}", _cdbPath);
        }
        else
        {
            try
            {
                _cdbPath = _pathDetectionService.GetBestDebuggerPath();
                _logger.LogInformation("Auto-detected debugger path: {Path}", _cdbPath);
            }
            catch (FileNotFoundException ex)
            {
                _logger.LogError("Failed to detect debugger path: {Error}", ex.Message);
                throw new InvalidOperationException("Cannot initialize SessionManagerService without valid debugger path", ex);
            }
        }

        _symbolCache = Environment.GetEnvironmentVariable("SYMBOL_CACHE")
                       ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CdbMcpServer", "symbols");
        _symbolPathExtra = Environment.GetEnvironmentVariable("SYMBOL_PATH_EXTRA") ?? "";

        _logger.LogInformation("CDB Configuration - Path: {CdbPath}, SymbolCache: {SymbolCache}, Extra: {Extra}",
                              _cdbPath, _symbolCache, _symbolPathExtra);
    }

    public async Task<string> CreateSessionWithDumpAsync(string dumpFilePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(dumpFilePath))
        {
            _logger.LogError("Dump file not found: {DumpFile}", dumpFilePath);
            throw new FileNotFoundException($"Dump file not found: {dumpFilePath}", dumpFilePath);
        }

        var sessionId = Guid.NewGuid().ToString("N")[..8];
        var sessionLogger = _loggerFactory.CreateLogger<CdbSessionService>();
        var session = new CdbSessionService(sessionId, sessionLogger, _analysisService, _cdbPath, _symbolCache, _symbolPathExtra);

        // Add session to dictionary first to prevent race conditions
        _sessions[sessionId] = session;
        _logger.LogInformation("Created new CDB session {SessionId}, loading dump: {DumpFile}", sessionId, dumpFilePath);

        try
        {
            await session.LoadDumpAsync(dumpFilePath, cancellationToken);
            
            // Verify session is still active after loading
            if (!session.IsActive)
            {
                _sessions.TryRemove(sessionId, out _);
                session.Dispose();
                throw new InvalidOperationException($"CDB process failed to start or exited during dump loading for session {sessionId}");
            }
            
            _logger.LogInformation("Successfully loaded dump in session {SessionId}: {DumpFile}", sessionId, dumpFilePath);
            return sessionId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load dump in session {SessionId}: {DumpFile}", sessionId, dumpFilePath);
            _sessions.TryRemove(sessionId, out _);
            session.Dispose();
            throw;
        }
    }

    public async Task<string> ExecuteCommandAsync(string sessionId, string command, CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            var error = $"Session {sessionId} not found";
            _logger.LogError(error);
            throw new ArgumentException(error, nameof(sessionId));
        }

        if (!session.IsActive)
        {
            var error = $"Session {sessionId} is not active";
            _logger.LogError(error);
            throw new InvalidOperationException(error);
        }

        try
        {
            return await session.ExecuteCommandAsync(command, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing command in session {SessionId}: {Command}", sessionId, command);
            throw;
        }
    }

    public async Task<string> ExecuteBasicAnalysisAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        return await ExecutePredefinedAnalysisAsync(sessionId, "basic", cancellationToken);
    }

    public async Task<string> ExecutePredefinedAnalysisAsync(string sessionId, string analysisName, CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            var error = $"Session {sessionId} not found";
            _logger.LogError(error);
            throw new ArgumentException(error, nameof(sessionId));
        }

        if (!session.IsActive)
        {
            var error = $"Session {sessionId} is not active";
            _logger.LogError(error);
            throw new InvalidOperationException(error);
        }

        try
        {
            return await session.ExecutePredefinedAnalysisAsync(analysisName, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing {AnalysisName} analysis in session {SessionId}", analysisName, sessionId);
            throw;
        }
    }

    public void CloseSession(string sessionId)
    {
        if (!_sessions.TryRemove(sessionId, out var session))
        {
            var error = $"Session {sessionId} not found";
            _logger.LogError(error);
            throw new ArgumentException(error, nameof(sessionId));
        }

        try
        {
            session.Dispose();
            _logger.LogInformation("Closed CDB session {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error closing session {SessionId}", sessionId);
            throw new InvalidOperationException($"Error closing session: {ex.Message}", ex);
        }
    }

    public IEnumerable<SessionInfo> GetActiveSessions()
    {
        return _sessions.Values.Select(s => new SessionInfo
        {
            SessionId = s.SessionId,
            DumpFile = s.CurrentDumpFile ?? "",
            IsActive = s.IsActive
        }).ToList();
    }

    public void Dispose()
    {
        foreach (var session in _sessions.Values)
        {
            try
            {
                session.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing session {SessionId}", session.SessionId);
            }
        }
        _sessions.Clear();
        GC.SuppressFinalize(this);
    }
}