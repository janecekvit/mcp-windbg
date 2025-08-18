using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace CdbBackgroundService;

public class CdbSessionManager : IDisposable
{
    private readonly ILogger<CdbSessionManager> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ConcurrentDictionary<string, CdbSession> _sessions = new();
    private readonly string _cdbPath;
    private readonly string _symbolCache;
    private readonly string _symbolPathExtra;

    public CdbSessionManager(ILogger<CdbSessionManager> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        
        // Auto-detekce CDB cesty nebo použití environment variable
        var envCdbPath = Environment.GetEnvironmentVariable("CDB_PATH");
        if (!string.IsNullOrEmpty(envCdbPath) && CdbPathDetector.ValidateDebuggerPath(envCdbPath, logger))
        {
            _cdbPath = envCdbPath;
            _logger.LogInformation("Using CDB path from environment variable: {Path}", _cdbPath);
        }
        else
        {
            try
            {
                _cdbPath = CdbPathDetector.GetBestDebuggerPath(logger);
                _logger.LogInformation("Auto-detected debugger path: {Path}", _cdbPath);
            }
            catch (FileNotFoundException ex)
            {
                _logger.LogError("Failed to detect debugger path: {Error}", ex.Message);
                throw new InvalidOperationException("Cannot initialize CdbSessionManager without valid debugger path", ex);
            }
        }
        
        _symbolCache = Environment.GetEnvironmentVariable("SYMBOL_CACHE") 
                       ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CdbMcpServer", "symbols");
        _symbolPathExtra = Environment.GetEnvironmentVariable("SYMBOL_PATH_EXTRA") ?? "";
        
        _logger.LogInformation("CDB Configuration - Path: {CdbPath}, SymbolCache: {SymbolCache}, Extra: {Extra}", 
                              _cdbPath, _symbolCache, _symbolPathExtra);
    }

    public async Task<(bool Success, string SessionId, string Message)> CreateSessionWithDumpAsync(string dumpFilePath)
    {
        if (!File.Exists(dumpFilePath))
        {
            return (false, "", $"Dump file not found: {dumpFilePath}");
        }

        var sessionId = Guid.NewGuid().ToString("N")[..8];
        var sessionLogger = _loggerFactory.CreateLogger<CdbSession>();
        var session = new CdbSession(sessionId, sessionLogger, _cdbPath, _symbolCache, _symbolPathExtra);

        var success = await session.LoadDumpAsync(dumpFilePath);
        if (!success)
        {
            session.Dispose();
            return (false, "", "Failed to load dump file into CDB session");
        }

        _sessions[sessionId] = session;
        _logger.LogInformation("Created new CDB session {SessionId} for dump: {DumpFile}", sessionId, dumpFilePath);
        
        return (true, sessionId, $"Session {sessionId} created successfully");
    }

    public async Task<(bool Success, string Message)> ExecuteCommandAsync(string sessionId, string command)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return (false, $"Session {sessionId} not found");
        }

        if (!session.IsActive)
        {
            return (false, $"Session {sessionId} is not active");
        }

        try
        {
            var result = await session.ExecuteCommandAsync(command);
            return (true, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing command in session {SessionId}: {Command}", sessionId, command);
            return (false, $"Error executing command: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> ExecuteBasicAnalysisAsync(string sessionId)
    {
        return await ExecutePredefinedAnalysisAsync(sessionId, "basic");
    }

    public async Task<(bool Success, string Message)> ExecutePredefinedAnalysisAsync(string sessionId, string analysisName)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return (false, $"Session {sessionId} not found");
        }

        if (!session.IsActive)
        {
            return (false, $"Session {sessionId} is not active");
        }

        try
        {
            var result = await session.ExecutePredefinedAnalysisAsync(analysisName);
            return (true, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing {AnalysisName} analysis in session {SessionId}", analysisName, sessionId);
            return (false, $"Error executing {analysisName} analysis: {ex.Message}");
        }
    }

    public (bool Success, string Message) CloseSession(string sessionId)
    {
        if (!_sessions.TryRemove(sessionId, out var session))
        {
            return (false, $"Session {sessionId} not found");
        }

        try
        {
            session.Dispose();
            _logger.LogInformation("Closed CDB session {SessionId}", sessionId);
            return (true, $"Session {sessionId} closed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error closing session {SessionId}", sessionId);
            return (false, $"Error closing session: {ex.Message}");
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
    }
}

public class SessionInfo
{
    public required string SessionId { get; init; }
    public required string DumpFile { get; init; }
    public required bool IsActive { get; init; }
}