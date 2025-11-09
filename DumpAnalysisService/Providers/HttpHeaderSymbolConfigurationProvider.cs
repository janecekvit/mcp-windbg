using Shared.Configuration;

namespace BackgroundService.Providers;

/// <summary>
/// Reads symbol configuration from HTTP headers with fallback to environment variables
/// This is a scoped service that reads per-request headers from MCP HTTP clients
/// </summary>
public class HttpHeaderSymbolConfigurationProvider : ISymbolConfigurationProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<HttpHeaderSymbolConfigurationProvider> _logger;

    public HttpHeaderSymbolConfigurationProvider(
        IHttpContextAccessor httpContextAccessor,
        ILogger<HttpHeaderSymbolConfigurationProvider> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public SymbolsConfiguration GetConfiguration()
    {
        var httpContext = _httpContextAccessor.HttpContext;

        string? symbolCache = null;
        string? symbolPathExtra = null;
        string? symbolServers = null;

        // Try to read from HTTP headers first (per-session configuration)
        if (httpContext != null)
        {
            if (httpContext.Request.Headers.TryGetValue("X-Symbol-Cache", out var cacheHeader))
            {
                symbolCache = cacheHeader.ToString();
                _logger.LogDebug("Symbol cache from header: {SymbolCache}", symbolCache);
            }

            if (httpContext.Request.Headers.TryGetValue("X-Symbol-Path-Extra", out var pathExtraHeader))
            {
                symbolPathExtra = pathExtraHeader.ToString();
                _logger.LogDebug("Symbol path extra from header: {SymbolPathExtra}", symbolPathExtra);
            }

            if (httpContext.Request.Headers.TryGetValue("X-Symbol-Servers", out var serversHeader))
            {
                symbolServers = serversHeader.ToString();
                _logger.LogDebug("Symbol servers from header: {SymbolServers}", symbolServers);
            }
        }

        var config = new SymbolsConfiguration(symbolCache, symbolPathExtra, symbolServers);

        _logger.LogInformation("Symbol configuration: Cache={Cache}, PathExtra={PathExtra}, Servers={Servers}",
            config.SymbolCache ?? "(default)",
            config.SymbolPathExtra ?? "(none)",
            config.SymbolServers ?? "(default)");

        return config;
    }
}
