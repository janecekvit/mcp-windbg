namespace Shared.Models;

/// <summary>
/// Represents different types of analysis that can be performed on memory dumps
/// </summary>
public enum AnalysisType
{
    /// <summary>
    /// Comprehensive basic crash analysis including exception context and thread stacks
    /// </summary>
    Basic,

    /// <summary>
    /// Detailed exception record and context analysis
    /// </summary>
    Exception,

    /// <summary>
    /// Thread enumeration and stack trace analysis
    /// </summary>
    Threads,

    /// <summary>
    /// Heap statistics and validation analysis
    /// </summary>
    Heap,

    /// <summary>
    /// Loaded and unloaded module analysis
    /// </summary>
    Modules,

    /// <summary>
    /// Process handle enumeration and analysis
    /// </summary>
    Handles,

    /// <summary>
    /// Critical section and deadlock detection analysis
    /// </summary>
    Locks,

    /// <summary>
    /// Virtual memory layout and usage analysis
    /// </summary>
    Memory,

    /// <summary>
    /// Device driver analysis and diagnostics
    /// </summary>
    Drivers,

    /// <summary>
    /// Process tree and detailed process information analysis
    /// </summary>
    Processes
}

/// <summary>
/// Extension methods for AnalysisType enum
/// </summary>
public static class AnalysisTypeExtensions
{
    /// <summary>
    /// Gets the string identifier used in APIs and commands
    /// </summary>
    /// <param name="analysisType">The analysis type</param>
    /// <returns>String identifier</returns>
    public static string ToString(this AnalysisType analysisType) => analysisType switch
    {
        AnalysisType.Basic => "basic",
        AnalysisType.Exception => "exception",
        AnalysisType.Threads => "threads",
        AnalysisType.Heap => "heap",
        AnalysisType.Modules => "modules",
        AnalysisType.Handles => "handles",
        AnalysisType.Locks => "locks",
        AnalysisType.Memory => "memory",
        AnalysisType.Drivers => "drivers",
        AnalysisType.Processes => "processes",
        _ => throw new ArgumentOutOfRangeException(nameof(analysisType), analysisType, null)
    };

    /// <summary>
    /// Parses a string identifier to AnalysisType
    /// </summary>
    /// <param name="identifier">String identifier</param>
    /// <returns>Corresponding AnalysisType</returns>
    /// <exception cref="ArgumentException">When identifier is invalid</exception>
    public static AnalysisType ToAnalysisType(this string identifier) => identifier.ToLowerInvariant() switch
    {
        "basic" => AnalysisType.Basic,
        "exception" => AnalysisType.Exception,
        "threads" => AnalysisType.Threads,
        "heap" => AnalysisType.Heap,
        "modules" => AnalysisType.Modules,
        "handles" => AnalysisType.Handles,
        "locks" => AnalysisType.Locks,
        "memory" => AnalysisType.Memory,
        "drivers" => AnalysisType.Drivers,
        "processes" => AnalysisType.Processes,
        _ => throw new ArgumentException($"Unknown analysis type: {identifier}", nameof(identifier))
    };

    /// <summary>
    /// Gets human-readable description of the analysis type
    /// </summary>
    /// <param name="analysisType">The analysis type</param>
    /// <returns>Description text</returns>
    public static string GetDescription(this AnalysisType analysisType) => analysisType switch
    {
        AnalysisType.Basic => "Complete crash analysis with exception context, call stacks, and basic system information",
        AnalysisType.Exception => "Detailed analysis of exception records, context, and error conditions",
        AnalysisType.Threads => "Analysis of all threads with call stacks and synchronization states",
        AnalysisType.Heap => "Heap memory analysis including statistics, corruption detection, and allocations",
        AnalysisType.Modules => "Analysis of loaded modules, symbols, and version information",
        AnalysisType.Handles => "Enumeration and analysis of process handles and resource usage",
        AnalysisType.Locks => "Detection and analysis of deadlocks, critical sections, and synchronization objects",
        AnalysisType.Memory => "Virtual memory layout, usage patterns, and memory allocation analysis",
        AnalysisType.Drivers => "Device driver analysis, loaded drivers, and driver-related diagnostics",
        AnalysisType.Processes => "Process tree, detailed process information, and inter-process relationships",
        _ => throw new ArgumentOutOfRangeException(nameof(analysisType), analysisType, null)
    };

    /// <summary>
    /// Gets all available analysis types
    /// </summary>
    /// <returns>Array of all AnalysisType values</returns>
    public static AnalysisType[] GetAll() => Enum.GetValues<AnalysisType>();

    /// <summary>
    /// Gets all analysis type identifiers
    /// </summary>
    /// <returns>Array of string identifiers</returns>
    public static IEnumerable<string> GetAllIdentifiers() => GetAll().Select(ToString).ToList();
}