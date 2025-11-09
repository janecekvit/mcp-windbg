using Microsoft.Extensions.Configuration;
using Shared.Configuration;

namespace Shared.Extensions;

/// <summary>
/// Extension methods for configuration management with environment variable fallback
/// </summary>
public static class ConfigurationExtensions
{
    /// <summary>
    /// Gets a typed configuration value with environment variable fallback
    /// </summary>
    /// <typeparam name="T">Type of the configuration value</typeparam>
    /// <param name="configuration">The configuration instance</param>
    /// <param name="key">Configuration key</param>
    /// <param name="environmentVariableName">Environment variable name for fallback</param>
    /// <param name="defaultValue">Default value if neither config nor env var is found</param>
    /// <returns>Typed configuration value, environment variable value, or default value</returns>
    public static T GetValueWithEnvironmentFallback<T>(
        this IConfiguration configuration,
        string key,
        string? environmentVariableName = null,
        T defaultValue = default!)
    {
        // Try configuration first
        try
        {
            var configValue = configuration.GetValue<T>(key);
            if (configValue != null && !EqualityComparer<T>.Default.Equals(configValue, default))
                return configValue;
        }
        catch
        {
            // If reading from configuration fails, continue to environment variable
        }

        // Fall back to environment variable
        if (environmentVariableName != null)
        {
            var envValue = Environment.GetEnvironmentVariable(environmentVariableName);
            if (!string.IsNullOrEmpty(envValue))
            {
                try
                {
                    // Handle nullable types
                    var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
                    var convertedValue = Convert.ChangeType(envValue, targetType);
                    return (T)convertedValue!;
                }
                catch
                {
                    // If parsing fails, fall through to default
                }
            }
        }

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
            DefaultSymbolCache = configuration.GetValueWithEnvironmentFallback<string?>(
                "Debugger:DefaultSymbolCache"),
            DefaultSymbolPathExtra = configuration.GetValueWithEnvironmentFallback<string?>(
                "Debugger:DefaultSymbolPathExtra"),
            DefaultSymbolServers = configuration.GetValueWithEnvironmentFallback<string?>(
                "Debugger:DefaultSymbolServers")
        };
    }
}