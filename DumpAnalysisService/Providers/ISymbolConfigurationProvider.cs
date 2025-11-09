using Shared.Configuration;
namespace DumpAnalysisService.Providers;

/// <summary>
/// Provider for symbol configuration that can read from different sources
/// (HTTP headers, environment variables, configuration files)
/// </summary>
public interface ISymbolConfigurationProvider
{
    /// <summary>
    /// Gets the current symbol configuration
    /// </summary>
    SymbolsConfiguration GetConfiguration();
}
