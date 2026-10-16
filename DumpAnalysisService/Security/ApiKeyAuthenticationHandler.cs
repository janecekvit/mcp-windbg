using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace DumpAnalysisService.Security;

/// <summary>
/// Authenticates the shared secret configured as Security:ApiKey, presented as
/// "X-Api-Key: &lt;key&gt;", "Authorization: Bearer &lt;key&gt;" or, for SignalR WebSocket clients that
/// cannot send headers, the "access_token" query parameter on /hubs. Constant-time comparison.
/// Does nothing when no key is configured.
/// </summary>
public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = Shared.Constants.Network.ApiKeyHeader;

    private readonly byte[]? _expectedKey;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptions<SecurityOptions> security)
        : base(options, logger, encoder)
    {
        var apiKey = security.Value.ApiKey;
        _expectedKey = string.IsNullOrWhiteSpace(apiKey) ? null : Encoding.UTF8.GetBytes(apiKey);
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (_expectedKey is null)
            return Task.FromResult(AuthenticateResult.NoResult());

        var presented = PresentedCredential(Request);
        if (presented is null)
            return Task.FromResult(AuthenticateResult.NoResult());

        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), _expectedKey))
        {
            Logger.LogWarning("Rejected an invalid API key: {Method} {Path} from {RemoteIp}",
                Request.Method, Request.Path, Context.Connection.RemoteIpAddress);
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, Caller.ApiKeyMethod), new Claim(Caller.AuthMethodClaim, Caller.ApiKeyMethod)],
            SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer";
        return Task.CompletedTask;
    }

    /// <summary>The API key or bearer token the request carries, or null.</summary>
    public static string? PresentedCredential(HttpRequest request)
    {
        if (request.Headers.TryGetValue(HeaderName, out var headerKey) && !string.IsNullOrEmpty(headerKey))
            return headerKey.ToString();

        return BearerToken(request);
    }

    /// <summary>"Authorization: Bearer &lt;token&gt;", or the access_token query parameter on /hubs.</summary>
    public static string? BearerToken(HttpRequest request)
    {
        var authorization = request.Headers.Authorization.ToString();
        const string bearerPrefix = "Bearer ";
        if (authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
            return authorization[bearerPrefix.Length..].Trim();

        if (request.Path.StartsWithSegments("/hubs") && request.Query.TryGetValue("access_token", out var token)
            && !string.IsNullOrEmpty(token))
            return token.ToString();

        return null;
    }
}
