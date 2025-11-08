namespace Shared.Configuration;

/// <summary>
/// Configuration settings for BackgroundService communication
/// </summary>
public class BackgroundServiceConfiguration
{
    /// <summary>
    /// Base URL for the BackgroundService HTTP API
    /// </summary>
    public required string BaseUrl { get; set; }
}