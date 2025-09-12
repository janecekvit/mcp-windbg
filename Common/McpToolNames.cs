namespace Common;

/// <summary>
/// Constants for MCP tool names used throughout the system
/// </summary>
public static class McpToolNames
{
    public const string LoadDump = "load_dump";
    public const string ExecuteCommand = "execute_command"; 
    public const string BasicAnalysis = "basic_analysis";
    public const string ListSessions = "list_sessions";
    public const string CloseSession = "close_session";
    public const string PredefinedAnalysis = "predefined_analysis";
    public const string ListAnalyses = "list_analyses";
    public const string DetectDebuggers = "detect_debuggers";
    
    /// <summary>
    /// Gets all available MCP tool names
    /// </summary>
    /// <returns>Array of all tool names</returns>
    public static string[] GetAll() => new[]
    {
        LoadDump,
        ExecuteCommand,
        BasicAnalysis,
        ListSessions,
        CloseSession,
        PredefinedAnalysis,
        ListAnalyses,
        DetectDebuggers
    };
}