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
    private static readonly AsyncLocal<(ClaimsPrincipal? User, string? McpSessionId)> _current = new();

    public static ClaimsPrincipal? User => _current.Value.User;

    /// <summary>
    /// The MCP session (Mcp-Session-Id) the call belongs to; null for stateless clients. Without
    /// authentication it is the owner of the sessions and jobs the call creates.
    /// </summary>
    public static string? McpSessionId => _current.Value.McpSessionId;

    /// <summary>Makes <paramref name="user"/> the current caller until the returned scope is disposed.</summary>
    public static IDisposable Use(ClaimsPrincipal? user, string? mcpSessionId = null)
    {
        var previous = _current.Value;
        _current.Value = (user, string.IsNullOrEmpty(mcpSessionId) ? null : mcpSessionId);
        return new Scope(previous);
    }

    private sealed class Scope((ClaimsPrincipal? User, string? McpSessionId) previous) : IDisposable
    {
        public void Dispose() => _current.Value = previous;
    }
}
