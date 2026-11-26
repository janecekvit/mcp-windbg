using System.Collections.Concurrent;
using DumpAnalysisService.Factories;
using DumpAnalysisService.Security;
using Microsoft.Extensions.Options;
using Shared;
using Shared.Models;

namespace DumpAnalysisService.Services;

public sealed class SessionManagerService : ISessionManagerService
{
    private readonly ILogger<SessionManagerService> _logger;
    private readonly ICdbSessionFactory _sessionFactory;
    private readonly IJobManagerService _jobManager;
    private readonly SecurityOptions _securityOptions;
    private readonly ConcurrentDictionary<string, ICdbSessionService> _sessions = new();
    private readonly ConcurrentDictionary<string, string> _owners = new();
    private readonly ConcurrentDictionary<string, SessionActivity> _activity = new();
    private readonly object _sessionsLock = new();
    private readonly TimeProvider _timeProvider;

    public SessionManagerService(
        ILogger<SessionManagerService> logger,
        ICdbSessionFactory sessionFactory,
        IJobManagerService jobManager,
        IOptions<SecurityOptions> securityOptions,
        TimeProvider? timeProvider = null)
    {
        _logger = logger;
        _sessionFactory = sessionFactory;
        _jobManager = jobManager;
        _securityOptions = securityOptions.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;

        _logger.LogInformation("SessionManagerService initialized with factory-based session creation");
    }

    public async Task<string> CreateSessionWithDumpAsync(
        string jobId,
        string dumpFilePath,
        Shared.Configuration.SymbolsConfiguration? symbols = null,
        string? owner = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _jobManager.UpdateProgressAsync(jobId, JobPhase.ValidatingInput, 0.05, "Validating dump file...");

            if (!File.Exists(dumpFilePath))
            {
                _logger.LogError("Dump file not found: {DumpFile}", dumpFilePath);
                throw new FileNotFoundException($"Dump file not found: {dumpFilePath}", dumpFilePath);
            }

            _EnsureDumpPathAllowed(dumpFilePath);

            await _jobManager.UpdateProgressAsync(jobId, JobPhase.StartingCdb, 0.1, "Creating CDB session...");

            var sessionId = Guid.NewGuid().ToString("N")[..Constants.Debugging.SessionIdLength];

            // Create session using factory with symbol configuration from MCP server
            var session = _sessionFactory.CreateSession(sessionId, symbols);

            // Register the session first so it can be cancelled while the dump loads;
            // the limit check and the insert happen atomically.
            lock (_sessionsLock)
            {
                if (_sessions.Count >= _securityOptions.MaxConcurrentSessions)
                {
                    session.Dispose();
                    throw new InvalidOperationException(
                        $"Maximum number of concurrent sessions ({_securityOptions.MaxConcurrentSessions}) reached; close a session first");
                }

                _sessions[sessionId] = session;
                if (owner is not null)
                    _owners[sessionId] = owner;
                _activity[sessionId] = new SessionActivity(_timeProvider.GetUtcNow());
            }

            _logger.LogInformation("Created new CDB session {SessionId}, loading dump: {DumpFile}", sessionId, dumpFilePath);

            try
            {
                // A loading session counts as busy: the idle sweeper leaves it alone.
                using var activity = _BeginActivity(sessionId);
                await session.LoadDumpAsync(dumpFilePath, _CreateProgressReporter(jobId), cancellationToken);

                // Verify session is still active after loading
                if (!session.IsActive)
                    throw new InvalidOperationException($"CDB process failed to start or exited during dump loading for session {sessionId}");
            }
            catch
            {
                // A failed load must not leave a cdb.exe process or a dead session behind
                _sessions.TryRemove(sessionId, out _);
                _owners.TryRemove(sessionId, out _);
                _activity.TryRemove(sessionId, out _);
                session.Dispose();
                throw;
            }

            await _jobManager.UpdateProgressAsync(jobId, JobPhase.Completed, 1.0, $"Session {sessionId} ready");

            _logger.LogInformation("Successfully loaded dump in session {SessionId}: {DumpFile}", sessionId, dumpFilePath);
            return sessionId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load dump: {DumpFile}", dumpFilePath);
            throw;
        }
    }

    public async Task<string> ExecuteCommandAsync(string jobId, string sessionId, string command, CancellationToken cancellationToken = default)
    {
        CdbCommandPolicy.Validate(command, _securityOptions.AllowUnsafeCommands);
        return await ExecuteInternalCommandAsync(jobId, sessionId, command, cancellationToken);
    }

    public bool TryGetSessionOwner(string sessionId, out string? owner)
    {
        owner = _owners.TryGetValue(sessionId, out var value) ? value : null;
        return _sessions.ContainsKey(sessionId);
    }

    public string? GetDumpFile(string sessionId) =>
        _sessions.TryGetValue(sessionId, out var session) ? session.CurrentDumpFile : null;

    public async Task<string> ExecuteInternalCommandAsync(string jobId, string sessionId, string command, CancellationToken cancellationToken = default)
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

        using var activity = _BeginActivity(sessionId);
        try
        {
            await _jobManager.UpdateProgressAsync(jobId, JobPhase.ExecutingCommand, 0.1, $"Executing command: {command}");

            var progressReporter = _CreateProgressReporter(jobId);

            var result = await session.ExecuteCommandAsync(command, progressReporter, cancellationToken);

            await _jobManager.UpdateProgressAsync(jobId, JobPhase.Completed, 1.0, "Command completed");

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing command in session {SessionId}: {Command}", sessionId, command);
            throw;
        }
    }

    public async Task<string> ExecuteBasicAnalysisAsync(string jobId, string sessionId, CancellationToken cancellationToken = default)
    {
        return await ExecutePredefinedAnalysisAsync(jobId, sessionId, AnalysisType.Basic, cancellationToken);
    }

    public async Task<string> ExecutePredefinedAnalysisAsync(string jobId, string sessionId, AnalysisType analysisType, CancellationToken cancellationToken = default)
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

        using var activity = _BeginActivity(sessionId);
        try
        {
            await _jobManager.UpdateProgressAsync(jobId, JobPhase.Analyzing, 0.1, $"Starting {analysisType.ToString()} analysis...");

            var progressReporter = _CreateProgressReporter(jobId);

            var result = await session.ExecutePredefinedAnalysisAsync(analysisType, progressReporter, cancellationToken);

            await _jobManager.UpdateProgressAsync(jobId, JobPhase.Completed, 1.0, "Analysis completed");

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing {AnalysisType} analysis in session {SessionId}", analysisType.ToString(), sessionId);
            throw;
        }
    }

    public async Task CancelSessionAsync(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            var error = $"Session {sessionId} not found";
            _logger.LogError(error);
            throw new ArgumentException(error, nameof(sessionId));
        }

        try
        {
            await session.CancelAsync();
            _sessions.TryRemove(sessionId, out _);
            _owners.TryRemove(sessionId, out _);
            _activity.TryRemove(sessionId, out _);
            session.Dispose();
            _logger.LogInformation("Cancelled and closed CDB session {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling session {SessionId}", sessionId);
            throw new InvalidOperationException($"Error cancelling session: {ex.Message}", ex);
        }
    }

    public async Task<IReadOnlyList<string>> CloseIdleSessionsAsync(TimeSpan idleTimeout, CancellationToken cancellationToken = default)
    {
        var closed = new List<string>();
        if (idleTimeout <= TimeSpan.Zero)
            return closed;

        var now = _timeProvider.GetUtcNow();
        foreach (var (sessionId, activity) in _activity)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Claimed under the lock: a command starting now sees the session closing and fails,
            // and a command already running keeps the session busy.
            if (!activity.TryClaimIdle(now, idleTimeout))
                continue;

            try
            {
                _logger.LogInformation("Closing session {SessionId}: idle for more than {IdleTimeout}", sessionId, idleTimeout);
                await CancelSessionAsync(sessionId);
                closed.Add(sessionId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to close idle session {SessionId}", sessionId);
            }
        }

        return closed;
    }

    /// <summary>Marks the session busy until disposed and records the activity time.</summary>
    /// <exception cref="InvalidOperationException">The session is being closed for inactivity.</exception>
    private IDisposable? _BeginActivity(string sessionId)
    {
        if (!_activity.TryGetValue(sessionId, out var activity))
            return null;

        activity.Begin(_timeProvider.GetUtcNow(), sessionId);
        return new ActivityScope(activity, _timeProvider);
    }

    private sealed class SessionActivity(DateTimeOffset created)
    {
        private readonly object _lock = new();
        private DateTimeOffset _lastActivity = created;
        private int _busy;
        private bool _closing;

        public void Begin(DateTimeOffset now, string sessionId)
        {
            lock (_lock)
            {
                if (_closing)
                    throw new InvalidOperationException($"Session {sessionId} is being closed");
                _busy++;
                _lastActivity = now;
            }
        }

        public void End(DateTimeOffset now)
        {
            lock (_lock)
            {
                _busy--;
                _lastActivity = now;
            }
        }

        public bool TryClaimIdle(DateTimeOffset now, TimeSpan idleTimeout)
        {
            lock (_lock)
            {
                if (_closing || _busy > 0 || now - _lastActivity < idleTimeout)
                    return false;
                _closing = true;
                return true;
            }
        }
    }

    private sealed class ActivityScope(SessionActivity activity, TimeProvider timeProvider) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                activity.End(timeProvider.GetUtcNow());
        }
    }

    /// <summary>
    /// Forwards session progress to the job. The handler is synchronous (no async void):
    /// a failed update is logged instead of crashing the process.
    /// </summary>
    private IProgress<ProgressUpdate> _CreateProgressReporter(string jobId) =>
        new Progress<ProgressUpdate>(update => _ = _ReportProgressSafeAsync(jobId, update));

    private async Task _ReportProgressSafeAsync(string jobId, ProgressUpdate update)
    {
        try
        {
            await _jobManager.UpdateProgressAsync(jobId, update.Phase, update.Progress, update.Message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to report progress for job {JobId}", jobId);
        }
    }

    private void _EnsureDumpPathAllowed(string dumpFilePath)
    {
        try
        {
            DumpPathPolicy.EnsureAllowed(dumpFilePath, _securityOptions);
        }
        catch (UnauthorizedAccessException)
        {
            _logger.LogWarning("Rejected dump outside Security:AllowedDumpDirectories: {DumpFile}", dumpFilePath);
            throw;
        }
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