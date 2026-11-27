using ModelContextProtocol;

namespace DumpAnalysisService.Tools;

/// <summary>
/// Which failures an MCP client or REST caller is told about. The MCP SDK replaces the message of
/// any exception other than <see cref="McpException"/> with a generic one, so client errors (invalid
/// arguments, a command the policy refuses, a session or dump that does not exist, a state the
/// caller can act on) become an <see cref="McpException"/> with their reason; anything else stays
/// generic and goes to the service log.
/// </summary>
public static class ToolErrors
{
    public const string UnexpectedError = "Unexpected server error; see the service log";

    /// <summary>True for the exception types services throw for errors the caller can act on.</summary>
    public static bool IsClientError(Exception ex) => ex is
        McpException
        or ArgumentException
        or FileNotFoundException
        or DirectoryNotFoundException
        or UnauthorizedAccessException
        or InvalidDataException
        or InvalidOperationException
        or TimeoutException;

    /// <summary>The message a client may see for this failure.</summary>
    public static string ClientMessage(Exception ex) => IsClientError(ex) ? ex.Message : UnexpectedError;

    /// <summary>
    /// The exception to throw from a tool: client errors as <see cref="McpException"/> with their
    /// reason; null for an unexpected error, which the caller rethrows unchanged.
    /// </summary>
    public static McpException? ToMcpException(Exception ex) => ex switch
    {
        OperationCanceledException => null,
        McpException mcp => mcp,
        _ when IsClientError(ex) => new McpException(ex.Message, ex),
        _ => null,
    };
}
