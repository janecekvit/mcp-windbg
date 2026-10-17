using System.Security.Claims;

namespace DumpAnalysisService.Security;

/// <summary>What a caller may do; each level includes the ones below it.</summary>
public enum DumpPermission
{
    None = 0,

    /// <summary>List jobs, analyses and crash groups; read job status and progress.</summary>
    Read = 1,

    /// <summary>Triage, load dumps, run commands and analyses, fingerprint, close sessions, cancel jobs.</summary>
    Analyze = 2,

    /// <summary>Analyze, on every user's sessions and jobs.</summary>
    Admin = 3,
}

/// <summary>Authorization policy names, one per <see cref="DumpPermission"/>.</summary>
public static class DumpPolicies
{
    public const string Read = "Dumps.Read";
    public const string Analyze = "Dumps.Analyze";
    public const string Admin = "Dumps.Admin";
}

/// <summary>
/// The caller of a request, reduced to what ownership checks need. <see cref="Id"/> is null for an
/// unrestricted caller (no authentication configured, the API key, or an administrator), who sees
/// every session and job.
/// </summary>
public sealed record Caller(string? Id, string Name, DumpPermission Permission)
{
    /// <summary>The claim the API key handler adds; an API key holder is an administrator.</summary>
    public const string AuthMethodClaim = "auth_method";
    public const string ApiKeyMethod = "api-key";

    public static readonly Caller Anonymous = new(null, "anonymous", DumpPermission.Admin);

    /// <summary>No identity although authentication is required: owns nothing and may do nothing.</summary>
    public static readonly Caller Unidentified = new("unidentified", "unidentified", DumpPermission.None);

    public bool IsRestricted => Id is not null;

    /// <summary>Whether this caller may use a session or job owned by <paramref name="owner"/> (null: unowned).</summary>
    public bool Owns(string? owner) => !IsRestricted || string.Equals(owner, Id, StringComparison.Ordinal);

    /// <summary>
    /// Resolves the caller from the authenticated principal. Without any authentication configured,
    /// everyone is unrestricted, as before bearer tokens existed; with it, a missing or anonymous
    /// principal is <see cref="Unidentified"/> (fail closed).
    /// </summary>
    public static Caller From(ClaimsPrincipal? user, SecurityOptions security)
    {
        if (!security.AuthenticationRequired)
            return Anonymous;
        if (user?.Identity?.IsAuthenticated != true)
            return Unidentified;

        if (user.HasClaim(AuthMethodClaim, ApiKeyMethod))
            return new Caller(null, ApiKeyMethod, DumpPermission.Admin);

        var permission = PermissionOf(user, security.Jwt);
        // Entra ID puts the stable user/app id in "oid"; other providers only have "sub".
        var id = user.FindFirst("oid")?.Value ?? user.FindFirst("sub")?.Value;
        var name = user.FindFirst("preferred_username")?.Value
            ?? user.FindFirst("upn")?.Value
            ?? user.FindFirst("name")?.Value
            ?? user.FindFirst("azp")?.Value
            ?? user.FindFirst("appid")?.Value
            ?? id
            ?? "unknown";

        // Administrators see everything; a non-admin token without a subject gets an id that owns only
        // what it creates itself.
        if (permission == DumpPermission.Admin)
            return new Caller(null, name, permission);
        return new Caller(id ?? $"unidentified:{name}", name, permission);
    }

    /// <summary>The highest role the principal holds, from any of the configured role claim types.</summary>
    public static DumpPermission PermissionOf(ClaimsPrincipal user, JwtOptions jwt)
    {
        var values = jwt.RoleClaimTypes
            .SelectMany(type => user.FindAll(type))
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToHashSet(StringComparer.Ordinal);

        if (values.Contains(jwt.AdminRole))
            return DumpPermission.Admin;
        if (values.Contains(jwt.AnalystRole))
            return DumpPermission.Analyze;
        if (values.Contains(jwt.ReaderRole))
            return DumpPermission.Read;
        return DumpPermission.None;
    }
}
