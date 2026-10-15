namespace DumpAnalysisService.Security;

/// <summary>
/// Bearer-token validation bound from "Security:Jwt". The service is an OAuth 2.1 resource
/// server only: it validates access tokens, it never signs users in.
/// </summary>
public sealed class JwtOptions
{
    /// <summary>
    /// The token issuer's authority; its OpenID configuration supplies the signing keys and the
    /// expected issuer. Entra ID: https://login.microsoftonline.com/{tenant-id}/v2.0.
    /// Setting it enables bearer tokens.
    /// </summary>
    public string? Authority { get; set; }

    /// <summary>Accepted "aud" values. Entra ID: the API app's client id and/or api://{client-id}.</summary>
    public List<string> Audiences { get; set; } = [];

    /// <summary>
    /// Accepted "iss" values. Empty: the issuer from the authority's metadata. Entra ID v1 tokens
    /// (https://sts.windows.net/{tenant-id}/) need it unless the API requests v2 tokens.
    /// </summary>
    public List<string> ValidIssuers { get; set; } = [];

    /// <summary>
    /// The externally reachable base URL (e.g. https://dumps.example.com) advertised as the
    /// protected resource. Empty: inferred from each request (behind a proxy, forward the headers).
    /// </summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>Scopes advertised in the protected resource metadata (Entra ID: api://{client-id}/Dumps.Analyze).</summary>
    public List<string> ScopesSupported { get; set; } = [];

    /// <summary>
    /// Claims whose (space-separated) values are matched against the role names. The default is
    /// Entra ID app roles only: a delegated scope is granted by user consent, not by an assignment,
    /// so add "scp"/"scope" only for providers that put permissions there (Auth0, Keycloak).
    /// </summary>
    public List<string> RoleClaimTypes { get; set; } = ["roles"];

    /// <summary>List jobs, analyses and crash groups; read job status and progress.</summary>
    public string ReaderRole { get; set; } = "Dumps.Read";

    /// <summary>Reader, plus triage, load dumps, run commands and analyses, fingerprint, close sessions.</summary>
    public string AnalystRole { get; set; } = "Dumps.Analyze";

    /// <summary>Analyst, plus every user's sessions and jobs.</summary>
    public string AdminRole { get; set; } = "Dumps.Admin";

    /// <summary>Require https for the authority's metadata (turn off only for a local test issuer).</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    public bool Enabled => !string.IsNullOrWhiteSpace(Authority);
}
