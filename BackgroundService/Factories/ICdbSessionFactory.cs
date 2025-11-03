using BackgroundService.Services;

namespace BackgroundService.Factories;

/// <summary>
/// Factory for creating CDB session instances with proper infrastructure dependencies.
/// </summary>
public interface ICdbSessionFactory
{
    /// <summary>
    /// Creates a new CDB session service instance with symbol configuration from MCP server
    /// </summary>
    /// <param name="sessionId">Unique session identifier</param>
    /// <param name="symbols">Optional: Symbol configuration from MCP server</param>
    /// <returns>Configured CDB session service</returns>
    ICdbSessionService CreateSession(
        string sessionId,
        Shared.Configuration.SymbolsConfiguration? symbols = null);
}
