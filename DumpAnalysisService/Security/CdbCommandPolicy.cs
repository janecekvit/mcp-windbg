using System.Text.RegularExpressions;

namespace DumpAnalysisService.Security;

/// <summary>
/// Validates user-supplied CDB commands before they reach the debugger's stdin.
/// Internal commands (session initialisation, predefined analyses) do not pass through here.
/// </summary>
public static partial class CdbCommandPolicy
{
    public const int MaxCommandLength = 4096;

    /// <summary>
    /// Debugger meta-commands that act outside the dump: start or attach to processes,
    /// load code, write files, run script files, end the session or change debugger paths.
    /// </summary>
    private static readonly string[] _unsafeMetaCommands =
    [
        ".shell", ".load", ".loadby", ".unload", ".unloadall", ".extpath", ".setdll",
        ".create", ".createdir", ".attach", ".detach", ".kill", ".restart", ".abandon", ".opendump",
        ".writemem", ".readmem", ".dump", ".logopen", ".logappend", ".logclose", ".dumpcab",
        "lsf", // loads an arbitrary file as source; ls/lsa then print it
        ".server", ".servers", ".remote", ".endsrv", ".noshell",
        ".sympath", ".symfix", ".srcpath", ".srcfix", ".exepath", ".lsrcpath",
        ".cmdtree", ".wtitle", ".outmask", ".ocommand", ".pcmd",
    ];

    // Script-file execution: $<file, $><file, $$<file, $$><file, $$>a<file
    [GeneratedRegex(@"\${1,2}>?a?<", RegexOptions.CultureInvariant)]
    private static partial Regex _ScriptFileRegex();

    // .foreach /f reads tokens from an arbitrary file (options precede the opening parenthesis)
    [GeneratedRegex(@"\.foreach\b[^(]*/f\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex _ForeachFileRegex();

    // A statement that quits the debugger: q, qq, qd, qq; ... (CDB stops parsing at ';')
    [GeneratedRegex(@"^\s*q[qd]?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex _QuitRegex();

    /// <summary>
    /// Throws <see cref="ArgumentException"/> when the command is not allowed.
    /// </summary>
    public static void Validate(string command, bool allowUnsafeCommands)
    {
        if (string.IsNullOrWhiteSpace(command))
            throw new ArgumentException("Command is required", nameof(command));

        if (command.Length > MaxCommandLength)
            throw new ArgumentException($"Command exceeds {MaxCommandLength} characters", nameof(command));

        // One command line per request: a line break would let a single request inject
        // further debugger input lines (and break the output markers).
        if (command.Any(c => char.IsControl(c) && c != '\t'))
            throw new ArgumentException("Command must be a single line without control characters", nameof(command));

        foreach (var statement in command.Split(';'))
        {
            if (_QuitRegex().IsMatch(statement))
                throw new ArgumentException("Quitting the debugger is not allowed; use close_session instead", nameof(command));
        }

        if (allowUnsafeCommands)
            return;

        var blocked = FindUnsafeToken(command);
        if (blocked != null)
            throw new ArgumentException(
                $"Command '{blocked}' is blocked by the server's command policy (Security:AllowUnsafeCommands=false)",
                nameof(command));
    }

    /// <summary>
    /// Returns the first unsafe meta-command found anywhere in the command line, or null.
    /// The whole line is scanned (not only the first token) because CDB accepts several
    /// statements separated by ';' and nests commands in .if / .foreach / .block / ~* e.
    /// </summary>
    public static string? FindUnsafeToken(string command)
    {
        if (_ScriptFileRegex().IsMatch(command))
            return "$<";

        if (_ForeachFileRegex().IsMatch(command))
            return ".foreach /f";

        foreach (var meta in _unsafeMetaCommands)
        {
            var index = 0;
            while ((index = command.IndexOf(meta, index, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                var end = index + meta.Length;
                var startsToken = index == 0 || !_IsTokenChar(command[index - 1]);
                var endsToken = end == command.Length || !_IsTokenChar(command[end]);
                var isWholeToken = startsToken && endsToken;
                if (isWholeToken)
                    return meta;
                index = end;
            }
        }

        return null;
    }

    private static bool _IsTokenChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '.' or '!';
}
