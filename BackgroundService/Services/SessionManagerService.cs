using System.Collections.Concurrent;
using Shared;
using Shared.Extensions;
using Shared.Models;

namespace BackgroundService.Services;

public sealed class SessionManagerService : ISessionManagerService
{
    private readonly ILogger<SessionManagerService> _logger;
    private readonly ICdbSessionFactory _sessionFactory;
    private readonly IJobManagerService _jobManager;
    private readonly ConcurrentDictionary<string, ICdbSessionService> _sessions = new();

    public SessionManagerService(
        ILogger<SessionManagerService> logger,
        ICdbSessionFactory sessionFactory,
        IJobManagerService jobManager)
    {
        _logger = logger;
        _sessionFactory = sessionFactory;
        _jobManager = jobManager;

        _logger.LogInformation("SessionManagerService initialized with factory-based session creation");
    }

    public async Task<string> CreateSessionWithDumpAsync(string jobId, string dumpFilePath, CancellationToken cancellationToken = default)
    {
        try
        {
            await _jobManager.UpdatePhaseAsync(jobId, JobPhase.ValidatingInput, "Validating dump file...");
            await _jobManager.UpdateProgressAsync(jobId, 0.05, "Validating dump file...");

            if (!File.Exists(dumpFilePath))
            {
                _logger.LogError("Dump file not found: {DumpFile}", dumpFilePath);
                throw new FileNotFoundException($"Dump file not found: {dumpFilePath}", dumpFilePath);
            }

            await _jobManager.UpdateProgressAsync(jobId, 0.1, "Creating CDB session...");

            var sessionId = Guid.NewGuid().ToString("N")[..Constants.Debugging.SessionIdLength];

            // Create structured progress reporter for CDB session
            var progressReporter = new Progress<ProgressUpdate>(async update =>
            {
                await _jobManager.UpdatePhaseAsync(jobId, update.Phase, update.Message);
                await _jobManager.UpdateProgressAsync(jobId, update.Progress, update.Message);
            });

            // Create session using factory (handles infrastructure dependencies)
            var session = _sessionFactory.CreateSession(sessionId);

            // Add session to dictionary first to prevent race conditions
            _sessions[sessionId] = session;
            _logger.LogInformation("Created new CDB session {SessionId}, loading dump: {DumpFile}", sessionId, dumpFilePath);

            await session.LoadDumpAsync(dumpFilePath, progressReporter, cancellationToken);

            // Verify session is still active after loading
            if (!session.IsActive)
            {
                _sessions.TryRemove(sessionId, out _);
                session.Dispose();
                throw new InvalidOperationException($"CDB process failed to start or exited during dump loading for session {sessionId}");
            }

            await _jobManager.UpdatePhaseAsync(jobId, JobPhase.Completed, $"Session {sessionId} ready");
            await _jobManager.UpdateProgressAsync(jobId, 1.0, $"Session {sessionId} ready");

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
            await _jobManager.UpdatePhaseAsync(jobId, JobPhase.ExecutingCommand, $"Executing command: {command}");
            await _jobManager.UpdateProgressAsync(jobId, 0.1, $"Executing command: {command}");

            // Create structured progress reporter
            var progressReporter = new Progress<ProgressUpdate>(async update =>
            {
                await _jobManager.UpdatePhaseAsync(jobId, update.Phase, update.Message);
                await _jobManager.UpdateProgressAsync(jobId, update.Progress, update.Message);
            });

            var result = await session.ExecuteCommandAsync(command, progressReporter, cancellationToken);

            await _jobManager.UpdatePhaseAsync(jobId, JobPhase.Completed, "Command completed");
            await _jobManager.UpdateProgressAsync(jobId, 1.0, "Command completed");

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
        return await ExecutePredefinedAnalysisAsync(jobId, sessionId, "basic", cancellationToken);
    }

    public async Task<string> ExecutePredefinedAnalysisAsync(string jobId, string sessionId, string analysisName, CancellationToken cancellationToken = default)
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
            await _jobManager.UpdatePhaseAsync(jobId, JobPhase.Analyzing, $"Starting {analysisName} analysis...");
            await _jobManager.UpdateProgressAsync(jobId, 0.1, $"Starting {analysisName} analysis...");

            // Create structured progress reporter
            var progressReporter = new Progress<ProgressUpdate>(async update =>
            {
                await _jobManager.UpdatePhaseAsync(jobId, update.Phase, update.Message);
                await _jobManager.UpdateProgressAsync(jobId, update.Progress, update.Message);
            });

            var result = await session.ExecutePredefinedAnalysisAsync(analysisName, progressReporter, cancellationToken);

            await _jobManager.UpdatePhaseAsync(jobId, JobPhase.Completed, "Analysis completed");
            await _jobManager.UpdateProgressAsync(jobId, 1.0, "Analysis completed");

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing {AnalysisName} analysis in session {SessionId}", analysisName, sessionId);
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
            session.Dispose();
            _logger.LogInformation("Cancelled and closed CDB session {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling session {SessionId}", sessionId);
            throw new InvalidOperationException($"Error cancelling session: {ex.Message}", ex);
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