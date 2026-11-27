using DumpAnalysisService.Security;
using Shared.Configuration;

namespace DumpAnalysisService.Services;

/// <summary>
/// Closes sessions that have been idle for Debugger:SessionIdleTimeout (no load, command or
/// analysis), through the normal close path, so an abandoned session does not keep a cdb.exe
/// process and its memory. Never closes a session while a command runs. Off when the timeout is zero.
/// </summary>
public sealed class IdleSessionSweeper : BackgroundService
{
    private static readonly Caller ServiceCaller = new(null, "service", DumpPermission.Admin);
    private static readonly TimeSpan MaxSweepInterval = TimeSpan.FromMinutes(1);

    private readonly ISessionManagerService _sessions;
    private readonly AuditLog _audit;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<IdleSessionSweeper> _logger;
    private readonly TimeSpan _idleTimeout;

    public IdleSessionSweeper(
        ISessionManagerService sessions,
        DebuggerConfiguration debuggerConfig,
        AuditLog audit,
        ILogger<IdleSessionSweeper> logger,
        TimeProvider? timeProvider = null)
    {
        _sessions = sessions;
        _audit = audit;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _idleTimeout = debuggerConfig.SessionIdleTimeout;
    }

    /// <summary>How often the sweeper looks: a quarter of the timeout, at most once a minute apart.</summary>
    public static TimeSpan SweepInterval(TimeSpan idleTimeout)
    {
        var quarter = idleTimeout / 4;
        if (quarter < TimeSpan.FromSeconds(1))
            return TimeSpan.FromSeconds(1);
        return quarter < MaxSweepInterval ? quarter : MaxSweepInterval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_idleTimeout <= TimeSpan.Zero)
        {
            _logger.LogInformation("Idle session timeout is off (Debugger:SessionIdleTimeout=0)");
            return;
        }

        _logger.LogInformation("Sessions idle for {IdleTimeout} are closed (Debugger:SessionIdleTimeout)", _idleTimeout);

        using var timer = new PeriodicTimer(SweepInterval(_idleTimeout), _timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await SweepAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>One pass: closes the idle sessions and records each in the audit log.</summary>
    public async Task SweepAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            foreach (var sessionId in await _sessions.CloseIdleSessionsAsync(_idleTimeout, cancellationToken))
                _audit.Record(ServiceCaller, "close_idle_session", sessionId, $"idle > {_idleTimeout}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Idle session sweep failed");
        }
    }
}
