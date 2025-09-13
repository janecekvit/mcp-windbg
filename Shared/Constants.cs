namespace Shared;

/// <summary>
/// Application-wide constants to eliminate magic numbers and strings
/// </summary>
public static class Constants
{
    /// <summary>
    /// Default port numbers and timeouts
    /// </summary>
    public static class Network
    {
        public const int DefaultBackgroundServicePort = 8080;
        public const string DefaultBackgroundServiceUrl = "http://localhost:8080";
    }

    /// <summary>
    /// HTTP status codes used throughout the application
    /// </summary>
    public static class Http
    {
        public const int BadRequest = 400;
        public const int NotFound = 404;
        public const int InternalServerError = 500;
    }

    /// <summary>
    /// MCP protocol error codes
    /// </summary>
    public static class McpErrorCodes
    {
        public const int ParseError = -32700;
        public const int InvalidRequest = -32600;
        public const int MethodNotFound = -32601;
        public const int InvalidParams = -32602;
        public const int InternalError = -32603;
        public const int ServerNotInitialized = -32002;
    }

    /// <summary>
    /// CDB and debugging-related constants
    /// </summary>
    public static class Debugging
    {
        public const int SessionIdLength = 8;
        public const int CommandTimeout = 30000; // 30 seconds
        public const int ProcessWaitTimeout = 5000; // 5 seconds
        public const int InitializationDelay = 500; // 0.5 seconds
        public const int ReadBufferSize = 4096;
        public const int LogTruncateLength = 100;
        public const int PollingDelay = 50; // milliseconds
    }

    /// <summary>
    /// MCP protocol version
    /// </summary>
    public static class Mcp
    {
        public const string ProtocolVersion = "2024-11-05";
    }

    /// <summary>
    /// Windows SDK versions for debugger detection
    /// </summary>
    public static class WindowsSdk
    {
        public static readonly string[] SupportedVersions = { "10", "11" };
    }

    /// <summary>
    /// Provides a collection of constants representing the names of tools available in the MCP (Managed Control
    /// Protocol) system.
    /// </summary>
    /// <remarks>This class contains predefined string constants for various MCP tool names, which can be used
    /// to reference specific tools in the system. It also provides a method to retrieve all available tool names as an
    /// array.</remarks>
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
}