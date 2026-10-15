using DumpAnalysisService.Services;

namespace DumpAnalysisService.Security;

/// <summary>
/// Sessions and jobs belong to the caller who created them. A restricted caller (a non-admin
/// bearer token) sees another caller's session or job as not found.
/// </summary>
public static class Ownership
{
    /// <exception cref="ArgumentException">The session exists but belongs to someone else.</exception>
    public static void EnsureSessionAccess(this ISessionManagerService sessions, Caller caller, string sessionId)
    {
        if (caller.IsRestricted && sessions.TryGetSessionOwner(sessionId, out var owner) && !caller.Owns(owner))
            throw new ArgumentException($"Session {sessionId} not found", nameof(sessionId));
    }

    /// <summary>False when the job does not exist or belongs to someone else.</summary>
    public static bool CanAccessJob(this IJobManagerService jobs, Caller caller, string jobId) =>
        jobs.TryGetJobOwner(jobId, out var owner) && caller.Owns(owner);
}

/// <summary>
/// Who loaded which dump, ran which command or analysis, and closed which session
/// (log category DumpAnalysisService.Security.AuditLog).
/// </summary>
public sealed class AuditLog
{
    private readonly ILogger<AuditLog> _logger;

    public AuditLog(ILogger<AuditLog> logger) => _logger = logger;

    public void Record(Caller caller, string action, string? sessionId = null, string? detail = null) =>
        _logger.LogInformation("AUDIT {Caller} ({CallerId}) {Action} session={SessionId} {Detail}",
            caller.Name, caller.Id ?? "-", action, sessionId ?? "-", detail ?? "");
}
