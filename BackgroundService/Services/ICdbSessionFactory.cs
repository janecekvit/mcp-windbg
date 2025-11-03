namespace BackgroundService.Services;

/// <summary>
/// Factory for creating CDB session instances with proper infrastructure dependencies.
/// </summary>
public interface ICdbSessionFactory
{
    /// <summary>
    /// Creates a new CDB session service instance with per-session symbol configuration
    /// </summary>
    /// <param name="sessionId">Unique session identifier</param>
    /// <param name="symbolCache">Optional: Symbol cache directory (per-session override)</param>
    /// <param name="symbolPathExtra">Optional: Additional local symbol paths</param>
    /// <param name="symbolServers">Optional: Custom symbol servers</param>
    /// <returns>Configured CDB session service</returns>
    ICdbSessionService CreateSession(
        string sessionId,
        string? symbolCache = null,
        string? symbolPathExtra = null,
        string? symbolServers = null);
}
