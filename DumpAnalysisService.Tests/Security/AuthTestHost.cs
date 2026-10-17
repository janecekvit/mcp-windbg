using System.Net.Http.Headers;
using System.Security.Cryptography;
using DumpAnalysisService.Factories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Moq;

namespace DumpAnalysisService.Tests.Security;

/// <summary>
/// The real service in-process, with bearer tokens signed by a local RSA key instead of an
/// identity provider (the issuer's metadata is pre-set, so nothing is fetched).
/// </summary>
public sealed class AuthTestHost : WebApplicationFactory<Program>
{
    public const string Issuer = "https://issuer.test/tenant/v2.0";
    public const string Audience = "api://dump-analysis";
    public const string ApiKey = "test-api-key-123";

    private static readonly RsaSecurityKey SigningKey = new(RSA.Create(2048)) { KeyId = "test-key" };

    private readonly bool _jwt;
    private readonly string? _apiKey;

    public AuthTestHost(bool jwt = true, string? apiKey = ApiKey)
    {
        _jwt = jwt;
        _apiKey = apiKey;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        // UseSetting, not ConfigureAppConfiguration: Program reads Security:* before Build().
        builder.UseSetting("Security:ApiKey", _apiKey);
        builder.UseSetting("Logging:LogLevel:Microsoft.AspNetCore.Authentication", "Debug");
        builder.UseSetting("Fingerprint:RegistryPath", Path.Combine(Path.GetTempPath(), $"fingerprints-{Guid.NewGuid():N}.json"));
        if (_jwt)
        {
            builder.UseSetting("Security:Jwt:Authority", Issuer);
            builder.UseSetting("Security:Jwt:Audiences:0", Audience);
            builder.UseSetting("Security:Jwt:ScopesSupported:0", $"{Audience}/Dumps.Analyze");
        }

        // No cdb.exe on the test machine: sessions are never started here.
        builder.ConfigureServices(services => services.AddSingleton(new Mock<ICdbSessionFactory>().Object));

        if (_jwt)
        {
            builder.ConfigureServices(services => services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                configuration.SigningKeys.Add(SigningKey);
                options.Configuration = configuration;
                // JwtBearer validates through the configuration manager, so replace the one that would fetch metadata.
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            }));
        }
    }

    /// <summary>A signed access token; roles go into the Entra-style "roles" claim.</summary>
    public static string Token(string subject, string[]? roles = null, string audience = Audience, string? scope = null,
        DateTime? expires = null, string issuer = Issuer)
    {
        var claims = new Dictionary<string, object> { ["oid"] = subject, ["preferred_username"] = $"{subject}@example.test" };
        if (roles is { Length: > 0 })
            claims["roles"] = roles;
        if (scope is not null)
            claims["scp"] = scope;

        var now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = claims,
            NotBefore = now.AddMinutes(-5),
            IssuedAt = now.AddMinutes(-5),
            Expires = expires ?? now.AddHours(1),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256),
        });
    }

    public HttpClient ClientWithToken(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public HttpClient ClientWithApiKey()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        return client;
    }
}

/// <summary>
/// Every test class that starts the service in-process. The MCP SDK can lose the special binding
/// of a tool parameter (IProgress, RequestContext) when several servers build their tools at the
/// same time in one process, so these classes run one after another. A real deployment runs one
/// server per process.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class InProcessServiceCollection
{
    public const string Name = "In-process service";
}

