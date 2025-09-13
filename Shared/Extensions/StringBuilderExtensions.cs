using System.Text;

namespace Shared.Extensions;

public static class StringBuilderExtensions
{
    /// <summary>
    /// Appends a section header to the StringBuilder
    /// </summary>
    /// <param name="sb">The StringBuilder instance</param>
    /// <param name="title">Section title</param>
    /// <returns>The StringBuilder for chaining</returns>
    public static StringBuilder AppendSection(this StringBuilder sb, string title) =>
        sb.AppendLine(title).AppendLine();

    /// <summary>
    /// Appends a key-value pair with indentation
    /// </summary>
    /// <param name="sb">The StringBuilder instance</param>
    /// <param name="key">The key name</param>
    /// <param name="value">The value to append</param>
    /// <returns>The StringBuilder for chaining</returns>
    public static StringBuilder AppendKeyValue(this StringBuilder sb, string key, object? value) =>
        sb.AppendLine($"  {key}: {value}");

    /// <summary>
    /// Appends a line with indentation
    /// </summary>
    /// <param name="sb">The StringBuilder instance</param>
    /// <param name="text">Text to append with indentation</param>
    /// <returns>The StringBuilder for chaining</returns>
    public static StringBuilder AppendIndentedLine(this StringBuilder sb, string text) =>
        sb.AppendLine($"  {text}");
}