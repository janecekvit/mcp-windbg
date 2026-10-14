using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace DumpAnalysisService.Security;

/// <summary>
/// Requires the configured API key on every request except the health probe.
/// Accepts "X-Api-Key: &lt;key&gt;", "Authorization: Bearer &lt;key&gt;" and, for SignalR
/// WebSocket clients that cannot send headers, the "access_token" query parameter on /hubs.
/// Does nothing when Security:ApiKey is not configured.
/// </summary>
public sealed class ApiKeyMiddleware
{
    public const string HeaderName = Shared.Constants.Network.ApiKeyHeader;
    public const string HealthPath = "/api/diagnostics/health";

    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyMiddleware> _logger;
    private readonly byte[]? _expectedKey;

    public ApiKeyMiddleware(RequestDelegate next, IOptions<SecurityOptions> options, ILogger<ApiKeyMiddleware> logger)
    {
        _next = next;
        _logger = logger;

        var apiKey = options.Value.ApiKey;
        _expectedKey = string.IsNullOrWhiteSpace(apiKey) ? null : Encoding.UTF8.GetBytes(apiKey);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (_expectedKey == null || context.Request.Path.StartsWithSegments(HealthPath))
        {
            await _next(context);
            return;
        }

        var presentedKey = _GetPresentedKey(context.Request);
        if (presentedKey != null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presentedKey), _expectedKey))
        {
            await _next(context);
            return;
        }

        _logger.LogWarning("Rejected request without a valid API key: {Method} {Path} from {RemoteIp}",
            context.Request.Method, context.Request.Path, context.Connection.RemoteIpAddress);

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = "Bearer";
    }

    private static string? _GetPresentedKey(HttpRequest request)
    {
        if (request.Headers.TryGetValue(HeaderName, out var headerKey) && !string.IsNullOrEmpty(headerKey))
            return headerKey.ToString();

        var authorization = request.Headers.Authorization.ToString();
        const string bearerPrefix = "Bearer ";
        if (authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
            return authorization[bearerPrefix.Length..].Trim();

        if (request.Path.StartsWithSegments("/hubs") && request.Query.TryGetValue("access_token", out var token))
            return token.ToString();

        return null;
    }
}
