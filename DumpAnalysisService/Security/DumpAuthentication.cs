using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.AspNetCore.Authentication;

namespace DumpAnalysisService.Security;

/// <summary>
/// Wires the API key and OAuth bearer tokens into ASP.NET Core authentication, and the
/// <see cref="DumpPolicies"/> into authorization. A request may carry either credential: a policy
/// scheme forwards a JWT-shaped bearer token to JwtBearer and anything else to the API key handler.
/// With bearer tokens enabled, a 401 points MCP clients at the protected resource metadata
/// (RFC 9728) that the MCP SDK serves at /.well-known/oauth-protected-resource.
/// </summary>
public static class DumpAuthentication
{
    public const string SelectorScheme = "DumpAnalysis";

    public static WebApplicationBuilder AddDumpAuthentication(this WebApplicationBuilder builder)
    {
        var security = builder.Configuration.GetSection(SecurityOptions.SectionName).Get<SecurityOptions>() ?? new SecurityOptions();
        var jwt = security.Jwt;
        if (jwt.Enabled && jwt.Audiences.Count == 0)
            throw new InvalidOperationException(
                "Security:Jwt:Audiences is required when Security:Jwt:Authority is set (Entra ID: the API app's client id or api://{client-id}).");

        var authentication = builder.Services.AddAuthentication(SelectorScheme)
            .AddPolicyScheme(SelectorScheme, "API key or bearer token", options =>
            {
                options.ForwardDefaultSelector = context => _SelectScheme(context.Request, jwt.Enabled);
                options.ForwardChallenge = jwt.Enabled ? McpAuthenticationDefaults.AuthenticationScheme : ApiKeyAuthenticationHandler.SchemeName;
            })
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
                ApiKeyAuthenticationHandler.SchemeName, null);

        if (jwt.Enabled)
        {
            authentication.AddJwtBearer(options =>
            {
                options.Authority = jwt.Authority;
                options.RequireHttpsMetadata = jwt.RequireHttpsMetadata;
                // Keep "oid", "roles", "scp" as the issuer wrote them.
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuers = jwt.ValidIssuers.Count > 0 ? jwt.ValidIssuers : null,
                    ValidateAudience = true,
                    ValidAudiences = jwt.Audiences,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    NameClaimType = "name",
                    RoleClaimType = "roles",
                };
                options.Events = new JwtBearerEvents
                {
                    // SignalR WebSocket clients cannot send headers.
                    OnMessageReceived = context =>
                    {
                        if (context.Request.Path.StartsWithSegments("/hubs")
                            && context.Request.Query.TryGetValue("access_token", out var token))
                            context.Token = token;
                        return Task.CompletedTask;
                    },
                };
            });

            authentication.AddMcp(options =>
            {
                options.ResourceMetadata = new()
                {
                    Resource = string.IsNullOrWhiteSpace(jwt.PublicBaseUrl) ? null : $"{jwt.PublicBaseUrl.TrimEnd('/')}/mcp",
                    AuthorizationServers = [jwt.Authority!],
                    ScopesSupported = [.. jwt.ScopesSupported],
                    BearerMethodsSupported = ["header"],
                    ResourceName = "WinDbg MCP Server",
                };
            });
        }

        builder.Services.AddSingleton<IAuthorizationHandler, DumpPermissionHandler>();
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(DumpPolicies.Read, policy => policy.AddRequirements(new DumpPermissionRequirement(DumpPermission.Read)))
            .AddPolicy(DumpPolicies.Analyze, policy => policy.AddRequirements(new DumpPermissionRequirement(DumpPermission.Analyze)))
            .AddPolicy(DumpPolicies.Admin, policy => policy.AddRequirements(new DumpPermissionRequirement(DumpPermission.Admin)));

        if (security.AuthenticationRequired)
        {
            // Every endpoint without its own [Authorize]/[AllowAnonymous] (MCP, SignalR) needs a credential.
            builder.Services.AddAuthorizationBuilder()
                .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        }

        return builder;
    }

    /// <summary>A JWT (three dot-separated parts) goes to JwtBearer; an API key, or no credential, to the API key handler.</summary>
    private static string _SelectScheme(HttpRequest request, bool jwtEnabled)
    {
        if (!jwtEnabled || request.Headers.ContainsKey(ApiKeyAuthenticationHandler.HeaderName))
            return ApiKeyAuthenticationHandler.SchemeName;

        var token = ApiKeyAuthenticationHandler.BearerToken(request);
        return token is not null && token.Count(c => c == '.') == 2
            ? JwtBearerDefaults.AuthenticationScheme
            : ApiKeyAuthenticationHandler.SchemeName;
    }
}

public sealed record DumpPermissionRequirement(DumpPermission Required) : IAuthorizationRequirement;

/// <summary>
/// Grants a <see cref="DumpPermissionRequirement"/> when no authentication is configured, to the API
/// key, or to a token holding the role (or a higher one).
/// </summary>
public sealed class DumpPermissionHandler : AuthorizationHandler<DumpPermissionRequirement>
{
    private readonly SecurityOptions _security;
    private readonly ILogger<DumpPermissionHandler> _logger;

    public DumpPermissionHandler(IOptions<SecurityOptions> security, ILogger<DumpPermissionHandler> logger)
    {
        _security = security.Value;
        _logger = logger;
    }

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, DumpPermissionRequirement requirement)
    {
        if (!_security.AuthenticationRequired)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (context.User.Identity?.IsAuthenticated != true)
            return Task.CompletedTask;

        var caller = Caller.From(context.User, _security);
        if (caller.Permission >= requirement.Required)
            context.Succeed(requirement);
        else
            _logger.LogWarning("Denied {Caller}: needs {Required}, has {Permission}", caller.Name, requirement.Required, caller.Permission);

        return Task.CompletedTask;
    }
}
