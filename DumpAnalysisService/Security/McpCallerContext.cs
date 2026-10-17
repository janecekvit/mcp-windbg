using System.Security.Claims;

namespace DumpAnalysisService.Security;

/// <summary>
/// The user of the MCP tool call in progress. A call-tool request filter sets it from the
/// request context (which the SDK keeps for task-backed calls too, after the HTTP request ends),
/// so tools do not need a RequestContext parameter: the SDK binds such a parameter from the
/// tool arguments instead when its binding is not applied, and a missing identity must never
/// read as an unrestricted caller.
/// </summary>
public static class McpCallerContext
{
    private static readonly AsyncLocal<ClaimsPrincipal?> _current = new();

    public static ClaimsPrincipal? User => _current.Value;

    /// <summary>Makes <paramref name="user"/> the current caller until the returned scope is disposed.</summary>
    public static IDisposable Use(ClaimsPrincipal? user)
    {
        var previous = _current.Value;
        _current.Value = user;
        return new Scope(previous);
    }

    private sealed class Scope(ClaimsPrincipal? previous) : IDisposable
    {
        public void Dispose() => _current.Value = previous;
    }
}
