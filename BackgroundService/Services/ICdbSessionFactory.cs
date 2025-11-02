namespace BackgroundService.Services;

/// <summary>
/// Factory for creating CDB session instances with proper infrastructure dependencies.
/// </summary>
public interface ICdbSessionFactory
{
    /// <summary>
    /// Creates a new CDB session service instance
    /// </summary>
    /// <param name="sessionId">Unique session identifier</param>
    /// <returns>Configured CDB session service</returns>
    ICdbSessionService CreateSession(string sessionId);
}
