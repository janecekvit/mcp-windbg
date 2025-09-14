using Microsoft.Extensions.Configuration;
using Shared.Configuration;

namespace Shared.Extensions;

/// <summary>
/// Extension methods for configuration management with environment variable fallback
/// </summary>
public static class ConfigurationExtensions
{
    /// <summary>
    /// Gets a configuration value with environment variable fallback
    /// </summary>
    /// <param name="configuration">The configuration instance</param>
    /// <param name="key">Configuration key</param>
    /// <param name="environmentVariableName">Environment variable name for fallback</param>
    /// <param name="defaultValue">Default value if neither config nor env var is found</param>
    /// <returns>Configuration value, environment variable value, or default value</returns>
    public static string? GetValueWithEnvironmentFallback(
        this IConfiguration configuration,
        string key,
        string environmentVariableName,
        string? defaultValue = null)
    {
        // Try configuration first
        var configValue = configuration[key];
        if (!string.IsNullOrEmpty(configValue))
            return configValue;

        // Fall back to environment variable
        var envValue = Environment.GetEnvironmentVariable(environmentVariableName);
        if (!string.IsNullOrEmpty(envValue))
            return envValue;

        // Return default value
        return defaultValue;
    }

    /// <summary>
    /// Gets debugger configuration with environment variable fallback
    /// </summary>
    /// <param name="configuration">The configuration instance</param>
    /// <returns>DebuggerConfiguration with values from config or environment variables</returns>
    public static DebuggerConfiguration GetDebuggerConfiguration(this IConfiguration configuration)
    {
        return new DebuggerConfiguration
        {
            CdbPath = configuration.GetValueWithEnvironmentFallback("Debugger:CdbPath", "CDB_PATH"),
            SymbolCache = configuration.GetValueWithEnvironmentFallback("Debugger:SymbolCache", "SYMBOL_CACHE"),
            SymbolPathExtra = configuration.GetValueWithEnvironmentFallback("Debugger:SymbolPathExtra", "SYMBOL_PATH_EXTRA", string.Empty)!,
            SymbolServers = configuration.GetValueWithEnvironmentFallback("Debugger:SymbolServers", "SYMBOL_SERVERS")
        };
    }

    /// <summary>
    /// Gets background service configuration with environment variable fallback
    /// </summary>
    /// <param name="configuration">The configuration instance</param>
    /// <returns>BackgroundServiceConfiguration with values from config or environment variables</returns>
    public static BackgroundServiceConfiguration GetBackgroundServiceConfiguration(this IConfiguration configuration)
    {
        return new BackgroundServiceConfiguration
        {
            BaseUrl = configuration.GetValueWithEnvironmentFallback("BackgroundService:BaseUrl", "BACKGROUND_SERVICE_URL", "http://localhost:8080")!
        };
    }
}