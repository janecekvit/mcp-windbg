using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DumpAnalysisService.Fingerprinting;

public enum FrameKind
{
    /// <summary>module!function: a symbol resolved the frame.</summary>
    Symbol,

    /// <summary>module+0xoffset: inside a module, but without a symbol.</summary>
    Offset,

    /// <summary>A bare address outside every module (JIT code, corrupt return address).</summary>
    Unknown,
}

/// <param name="Token">The normalised frame: "module!function", "module+0x1a2b" or "&lt;unknown&gt;"</param>
public sealed record StackFrame(string Module, string Token, FrameKind Kind);

/// <param name="Frames">Frames used, top first, for display (recursion as "f (×k)")</param>
/// <param name="HashInput">What was hashed: the exception code and the frames, recursion without its depth</param>
public sealed record FingerprintComputation(string Fingerprint, string Quality, IReadOnlyList<string> Frames, string HashInput);

/// <summary>
/// Reduces a faulting stack (cdb <c>.ecxr; kn</c> output) to a fingerprint that stays the same
/// across machines, ASLR layouts and rebuilds: frames shared by every crash (exception dispatch,
/// RaiseException, abort, WER, runtime throw helpers) are dropped by <see cref="FingerprintRules"/>,
/// each frame is reduced to module!function, recursion is collapsed, and the first N meaningful
/// frames plus the exception code are hashed.
/// </summary>
public sealed partial class CrashFingerprinter
{
    public const string Exact = "exact";
    public const string Partial = "partial";
    public const string Weak = "weak";
    public const string UnknownFrame = "<unknown>";

    private readonly FingerprintRules _rules;

    public CrashFingerprinter(FingerprintRules rules) => _rules = rules;

    /// <summary>
    /// Frames of a kn listing, top first. Lines before the "# Child-SP" / "# ChildEBP" header
    /// (.ecxr's registers and disassembly) are skipped.
    /// </summary>
    public static IReadOnlyList<StackFrame> ParseStack(string knOutput)
    {
        var frames = new List<StackFrame>();
        var inListing = false;
        foreach (var line in knOutput.Split('\n'))
        {
            if (_HeaderLine().IsMatch(line))
            {
                // A second listing (another kn) starts a new stack; keep the first.
                if (inListing && frames.Count > 0)
                    break;
                inListing = true;
                continue;
            }
            if (!inListing)
                continue;

            var match = _FrameLine().Match(line.TrimEnd('\r'));
            if (match.Success && NormalizeCallSite(match.Groups["site"].Value) is { } frame)
                frames.Add(frame);
        }
        return frames;
    }

    /// <summary>
    /// "CrashScenarios!Foo::Bar&lt;int&gt;(int)+0x1f [c:\src\a.cpp @ 12]" → "crashscenarios!Foo::Bar";
    /// "contoso+0x1a2b" → "contoso+0x1a2b"; a bare address → "&lt;unknown&gt;".
    /// </summary>
    public static StackFrame? NormalizeCallSite(string callSite)
    {
        var site = _SourceSuffix().Replace(callSite, "").Trim();
        if (site.Length == 0)
            return null;
        if (_BareAddress().IsMatch(site))
            return new StackFrame("", UnknownFrame, FrameKind.Unknown);

        var bang = site.IndexOf('!');
        if (bang < 0)
        {
            var offset = _ModuleOffset().Match(site);
            var module = (offset.Success ? offset.Groups["module"].Value : site).ToLowerInvariant();
            var hex = offset.Success ? offset.Groups["offset"].Value.ToLowerInvariant().TrimStart('0') : "";
            return new StackFrame(module, $"{module}+0x{(hex.Length == 0 ? "0" : hex)}", FrameKind.Offset);
        }

        var moduleName = site[..bang].Trim().ToLowerInvariant();
        var function = _TrailingOffset().Replace(site[(bang + 1)..], "");
        function = _StripBalanced(function, '<', '>');
        function = _StripTrailingArguments(function);
        function = _CompilerSuffix().Replace(function, "").Trim();
        return function.Length == 0
            ? new StackFrame(moduleName, $"{moduleName}+0x0", FrameKind.Offset)
            : new StackFrame(moduleName, $"{moduleName}!{function}", FrameKind.Symbol);
    }

    public FingerprintComputation Compute(string exceptionCode, IReadOnlyList<StackFrame> frames)
    {
        // Drop the frames every crash shares, then collapse recursion (and runs of unknown frames).
        var collapsed = new List<(StackFrame Frame, int Count)>();
        foreach (var frame in frames.Where(f => !_rules.IsIgnored(f)))
        {
            if (collapsed.Count > 0 && collapsed[^1].Frame.Token == frame.Token)
                collapsed[^1] = (collapsed[^1].Frame, collapsed[^1].Count + 1);
            else
                collapsed.Add((frame, 1));
        }

        var used = collapsed.Take(_rules.FrameCount).ToList();
        var display = used.Select(u => u.Count > 1 && u.Frame.Kind != FrameKind.Unknown ? $"{u.Frame.Token} (×{u.Count})" : u.Frame.Token).ToList();
        // The recursion depth depends on the stack size and the displayed frame limit; leave it out of the hash.
        var hashed = used.Select(u => u.Count > 1 && u.Frame.Kind != FrameKind.Unknown ? $"{u.Frame.Token} (recursive)" : u.Frame.Token);
        var hashInput = exceptionCode.ToUpperInvariant() + "\n" + string.Join("\n", hashed);
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput)))[..12];

        return new FingerprintComputation(fingerprint, _Quality(used), display, hashInput);
    }

    private static string _Quality(List<(StackFrame Frame, int Count)> used)
    {
        var symbolized = used.Count(u => u.Frame.Kind == FrameKind.Symbol);
        if (symbolized == 0)
            return Weak;
        // One frame is too little to tell crashes apart, unless it is a recursion (a stack overflow).
        if (used.Count < 2 && used[0].Count < 2)
            return Weak;
        return symbolized == used.Count ? Exact : Partial;
    }

    /// <summary>Removes every balanced &lt;...&gt; group (template arguments, lambda names).</summary>
    private static string _StripBalanced(string text, char open, char close)
    {
        var result = new StringBuilder(text.Length);
        var depth = 0;
        foreach (var c in text)
        {
            if (c == open)
                depth++;
            else if (c == close && depth > 0)
                depth--;
            else if (depth == 0)
                result.Append(c);
        }
        return depth == 0 ? result.ToString() : text;
    }

    /// <summary>Removes a trailing argument list "(int, char const *)".</summary>
    private static string _StripTrailingArguments(string function)
    {
        var trimmed = function.TrimEnd();
        if (!trimmed.EndsWith(')'))
            return function;
        var depth = 0;
        for (var i = trimmed.Length - 1; i >= 0; i--)
        {
            if (trimmed[i] == ')')
                depth++;
            else if (trimmed[i] == '(' && --depth == 0)
                return i == 0 ? function : trimmed[..i];
        }
        return function;
    }

    [GeneratedRegex(@"^\s*#\s+Child", RegexOptions.IgnoreCase)]
    private static partial Regex _HeaderLine();

    // "00 000000c3`77cff6b8 00007ff6`1a2b3c61     module!function+0x4" (x64), "00 0019f8c4 77b1c123 ..." (x86),
    // "01 (Inline Function) --------`-------- module!inlined"
    [GeneratedRegex(@"^\s*[0-9a-f]{2,}\s+(?:[0-9a-f]{8}(?:`[0-9a-f]{8})?\s+[0-9a-f]{8}(?:`[0-9a-f]{8})?|\(Inline Function\)\s+-{8}(?:`-{8})?)\s+(?<site>\S.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex _FrameLine();

    [GeneratedRegex(@"\s+\[[^\]]*@\s*\d+\]\s*$")]
    private static partial Regex _SourceSuffix();

    // "0x7ff61a2b3c44", "0x00007ff6`1a2b3c44", "00007ff6`1a2b3c44", "77b1c123"
    [GeneratedRegex(@"^(?:0x[0-9a-f]+(?:`[0-9a-f]+)?|[0-9a-f]{8}(?:`?[0-9a-f]{8})?)$", RegexOptions.IgnoreCase)]
    private static partial Regex _BareAddress();

    [GeneratedRegex(@"^(?<module>.+?)\+0x(?<offset>[0-9a-f]+)$", RegexOptions.IgnoreCase)]
    private static partial Regex _ModuleOffset();

    [GeneratedRegex(@"\+0x[0-9a-f]+$", RegexOptions.IgnoreCase)]
    private static partial Regex _TrailingOffset();

    // MSVC funclets: Foo$filt$0, Foo$fin$1, Foo$catch$2, Foo$dtor$3, Foo$unwind$4
    [GeneratedRegex(@"\$(?:filt|fin|catch|dtor|unwind)\$\w*$")]
    private static partial Regex _CompilerSuffix();
}
