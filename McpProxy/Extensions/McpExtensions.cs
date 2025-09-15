using System.Text;
using McpProxy.Models;
using Shared.Models;

namespace McpProxy.Extensions;

public static class McpExtensions
{
    /// <summary>
    /// Converts Result<T> to McpToolResult
    /// </summary>
    /// <param name="result">The Result to convert</param>
    /// <returns>McpToolResult with success or error</returns>
    public static McpToolResult ToMcpToolResult<T>(this OperationResult<T> result)
        => result.IsSuccess ? McpToolResult.Success(result.Value?.ToString() ?? "") : McpToolResult.Error(result.Error);

    /// <summary>
    /// Converts StringBuilder content to a successful McpToolResult
    /// </summary>
    /// <param name="sb">The StringBuilder instance</param>
    /// <returns>McpToolResult with the StringBuilder content</returns>
    public static McpToolResult ToMcpSuccess(this StringBuilder sb) =>
        McpToolResult.Success(sb.ToString());

    /// <summary>
    /// Converts StringBuilder content to an error McpToolResult
    /// </summary>
    /// <param name="sb">The StringBuilder instance</param>
    /// <returns>McpToolResult with the StringBuilder content as error</returns>
    public static McpToolResult ToMcpError(this StringBuilder sb) =>
        McpToolResult.Error(sb.ToString());
}