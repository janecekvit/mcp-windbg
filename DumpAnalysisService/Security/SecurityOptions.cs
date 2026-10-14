namespace DumpAnalysisService.Security;

/// <summary>
/// Security settings bound from the "Security" configuration section
/// (appsettings.json or environment variables such as Security__ApiKey).
/// </summary>
public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>
    /// Shared secret required on every REST, MCP and SignalR request
    /// (X-Api-Key header or "Authorization: Bearer &lt;key&gt;").
    /// When empty, authentication is disabled - acceptable only on loopback.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Allows debugger meta-commands that can run processes, load DLLs or write files
    /// (.shell, .load, .create, .writemem, script files, ...). Keep false unless the
    /// service is reachable only by fully trusted callers.
    /// </summary>
    public bool AllowUnsafeCommands { get; set; }

    /// <summary>
    /// Allows clients to override the symbol cache, extra paths and symbol servers per
    /// request (X-Symbol-* headers or the REST "symbols" body). When false, only the
    /// server-side Debugger:* defaults are used.
    /// </summary>
    public bool AllowClientSymbolOverrides { get; set; } = true;

    /// <summary>
    /// Directories dump files may be loaded from. Empty list = any path the service can read.
    /// </summary>
    public List<string> AllowedDumpDirectories { get; set; } = [];

    /// <summary>
    /// Maximum number of concurrently open CDB sessions (each one is a cdb.exe process).
    /// </summary>
    public int MaxConcurrentSessions { get; set; } = 10;
}
