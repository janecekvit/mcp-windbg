using System.Text;
using System.Text.RegularExpressions;

namespace DumpAnalysisService.Infrastructure.Debugger;

/// <summary>
/// Collects the output of one CDB command from raw stdout chunks.
/// The command is framed by two unique markers echoed on their own input lines:
/// <c>.echo START</c>, the command, <c>.echo END</c>. Everything before START is discarded,
/// so output left over from an earlier command that timed out cannot leak into this result,
/// and a command that comments out the rest of its line cannot swallow the END marker.
/// </summary>
public sealed partial class CdbOutputCollector
{
    private readonly string _startMarker;
    private readonly string _endMarker;
    private readonly StringBuilder _buffer = new();
    private bool _started;
    private int _searchFrom;

    public CdbOutputCollector(string startMarker, string endMarker)
    {
        _startMarker = startMarker;
        _endMarker = endMarker;
    }

    public bool IsComplete { get; private set; }

    public string Result { get; private set; } = string.Empty;

    // CDB prompts: "0:000> ", "0:000:x86> ", "kd> ", "0: kd> "
    [GeneratedRegex(@"^(?:\s*(?:\d+:\s?)?(?:\d+:\d+(?::\w+)?|kd)>\s?)+", RegexOptions.CultureInvariant)]
    private static partial Regex _LeadingPromptRegex();

    [GeneratedRegex(@"(?:(?:\d+:\s?)?(?:\d+:\d+(?::\w+)?|kd)>\s*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex _TrailingPromptRegex();

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
            _searchFrom = _buffer.Length;
            return false;
        }

        var output = _buffer.ToString(0, endIndex);
        output = _LeadingPromptRegex().Replace(output, string.Empty, 1);
        output = _TrailingPromptRegex().Replace(output, string.Empty, 1);
        Result = output.Trim();
        IsComplete = true;
        return true;
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
