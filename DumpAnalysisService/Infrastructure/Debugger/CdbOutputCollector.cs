using System.Text;
using System.Text.RegularExpressions;

namespace DumpAnalysisService.Infrastructure.Debugger;

/// <summary>
/// Collects the output of one CDB command from raw stdout chunks.
/// The command is framed by two unique markers echoed on their own input lines:
/// <c>.echo START</c>, the command, <c>.echo END</c>. Everything before START is discarded,
/// so output left over from an earlier command that timed out cannot leak into this result,
/// and a command that comments out the rest of its line cannot swallow the END marker.
/// The result is bounded: progress-only lines are dropped and output beyond
/// <c>maxOutputBytes</c> (UTF-8) is cut off with an explicit truncation note. The buffer never
/// holds much more than that limit, however much the command prints.
/// </summary>
public sealed partial class CdbOutputCollector
{
    /// <summary>Default for Debugger:MaxCommandOutputBytes.</summary>
    public const int DefaultMaxOutputBytes = 256 * 1024;

    private readonly string _startMarker;
    private readonly string _endMarker;
    private readonly int _maxOutputBytes;
    private readonly StringBuilder _buffer = new();
    private bool _started;
    private int _searchFrom;
    private long _omittedBytes;

    /// <param name="startMarker">Line echoed before the command.</param>
    /// <param name="endMarker">Line echoed after the command.</param>
    /// <param name="maxOutputBytes">Upper bound of the result in UTF-8 bytes; 0 or less: unbounded.</param>
    public CdbOutputCollector(string startMarker, string endMarker, int maxOutputBytes = DefaultMaxOutputBytes)
    {
        _startMarker = startMarker;
        _endMarker = endMarker;
        _maxOutputBytes = maxOutputBytes;
    }

    public bool IsComplete { get; private set; }

    public string Result { get; private set; } = string.Empty;

    // CDB prompts: "0:000> ", "0:000:x86> ", "kd> ", "0: kd> "
    [GeneratedRegex(@"^(?:\s*(?:\d+:\s?)?(?:\d+:\d+(?::\w+)?|kd)>\s?)+", RegexOptions.CultureInvariant)]
    private static partial Regex _LeadingPromptRegex();

    [GeneratedRegex(@"(?:(?:\d+:\s?)?(?:\d+:\d+(?::\w+)?|kd)>\s*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex _TrailingPromptRegex();

    // Lines that only report progress: "Mapping heap regions..." (!address), "Loading User Symbols",
    // "Loading unloaded module list", and rows of progress dots.
    [GeneratedRegex(@"^[ \t]*(?:Mapping [A-Za-z ,]+ regions\.\.\.|Loading (?:User|Kernel) Symbols|Loading unloaded module list|\.{3,})[ \t]*\r?(?:\n|$)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex _ProgressLineRegex();

    /// <summary>
    /// Appends a stdout chunk. Returns true once the END marker has been seen.
    /// </summary>
    public bool Append(string chunk)
    {
        if (IsComplete)
            return true;

        _buffer.Append(chunk);

        if (!_started)
        {
            var text = _buffer.ToString();
            var startIndex = text.IndexOf(_startMarker, StringComparison.Ordinal);
            if (startIndex < 0)
            {
                // Keep only a tail that could still hold the beginning of a split marker.
                var keep = Math.Min(text.Length, _startMarker.Length - 1);
                _buffer.Remove(0, text.Length - keep);
                return false;
            }

            // Drop everything up to and including the START line.
            var afterMarker = startIndex + _startMarker.Length;
            var lineEnd = text.IndexOf('\n', afterMarker);
            if (lineEnd < 0)
            {
                // The rest of the START line has not arrived yet; wait for it.
                _buffer.Remove(0, startIndex);
                return false;
            }

            _buffer.Remove(0, lineEnd + 1);
            _started = true;
            _searchFrom = 0;
        }

        // Search only the new tail (plus marker overlap) to keep large outputs linear.
        var from = Math.Max(0, _searchFrom - _endMarker.Length);
        var endIndex = _IndexOf(_buffer, _endMarker, from);
        if (endIndex < 0)
        {
            _DropBeyondLimit();
            _searchFrom = _buffer.Length;
            return false;
        }

        var output = _buffer.ToString(0, endIndex);
        output = _LeadingPromptRegex().Replace(output, string.Empty, 1);
        output = _TrailingPromptRegex().Replace(output, string.Empty, 1);
        output = _ProgressLineRegex().Replace(output, string.Empty);
        output = _Truncate(output.Trim());
        Result = _omittedBytes > 0
            ? $"{output}{Environment.NewLine}[output truncated: {_omittedBytes} bytes omitted]"
            : output;
        IsComplete = true;
        return true;
    }

    /// <summary>
    /// While the END marker has not arrived: keeps the first <c>maxOutputBytes</c> characters (at
    /// least as many bytes) and a tail long enough to hold a split END marker, counting what is dropped.
    /// Progress lines are filtered only at the end, so the kept part can be shorter than the limit.
    /// </summary>
    private void _DropBeyondLimit()
    {
        if (_maxOutputBytes <= 0)
            return;

        var tail = _endMarker.Length - 1;
        var excess = _buffer.Length - _maxOutputBytes - tail;
        if (excess <= 0)
            return;

        _omittedBytes += Encoding.UTF8.GetByteCount(_buffer.ToString(_maxOutputBytes, excess));
        _buffer.Remove(_maxOutputBytes, excess);
    }

    /// <summary>Cuts the result to <c>maxOutputBytes</c> UTF-8 bytes on a character boundary.</summary>
    private string _Truncate(string output)
    {
        if (_maxOutputBytes <= 0 || Encoding.UTF8.GetByteCount(output) <= _maxOutputBytes)
            return output;

        // Keep whole characters (never half of a surrogate pair) within the byte budget.
        var keptBytes = 0;
        var keptChars = 0;
        while (keptChars < output.Length)
        {
            var width = char.IsHighSurrogate(output[keptChars]) && keptChars + 1 < output.Length ? 2 : 1;
            var bytes = Encoding.UTF8.GetByteCount(output.AsSpan(keptChars, width));
            if (keptBytes + bytes > _maxOutputBytes)
                break;
            keptBytes += bytes;
            keptChars += width;
        }

        _omittedBytes += Encoding.UTF8.GetByteCount(output.AsSpan(keptChars));
        return output[..keptChars];
    }

    private static int _IndexOf(StringBuilder builder, string value, int startIndex)
    {
        var limit = builder.Length - value.Length;
        for (var i = startIndex; i <= limit; i++)
        {
            var match = true;
            for (var j = 0; j < value.Length; j++)
            {
                if (builder[i + j] != value[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
                return i;
        }

        return -1;
    }
}
