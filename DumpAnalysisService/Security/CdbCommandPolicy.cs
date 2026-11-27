using System.Collections.Frozen;
using System.Text;
using System.Text.RegularExpressions;

namespace DumpAnalysisService.Security;

/// <summary>
/// Validates user-supplied CDB commands before they reach the debugger's stdin.
/// Internal commands (session initialisation, predefined analyses) do not pass through here.
/// </summary>
/// <remarks>
/// The policy is an allowlist: the line is split into statements on ';' (outside quotes) and every
/// statement must start with a read-only inspection command. A blocklist cannot anticipate every way
/// debugger syntax composes a command; an allowlist fails closed. On top of that, syntax that builds
/// or runs further command text (aliases, nested command blocks, script files and script providers)
/// and meta-commands that act outside the dump are refused anywhere in the line, quoted text included.
/// </remarks>
public static partial class CdbCommandPolicy
{
    public const int MaxCommandLength = 4096;

    private const string AllowedCommandsHint =
        "only read-only inspection commands are accepted (Security:AllowedCommands adds commands)";

    /// <summary>
    /// Standard commands, meta-commands and expression evaluators accepted at the start of a statement.
    /// Command families written with option letters (k*, lm*) are matched by <see cref="_IsStandardFamily"/>.
    /// </summary>
    private static readonly FrozenSet<string> _allowedCommands = new[]
    {
        // Memory display
        "d", "da", "db", "dc", "dd", "df", "dp", "dq", "du", "dw", "dyb", "dyd",
        "dds", "dps", "dqs", "ddp", "dpp", "dqp", "dda", "ddu", "dpa", "dpu", "dqa", "dqu",
        "dt", "dtx", "dv", "dg", "dl", "ds", "dx", "s",
        // Disassembly, symbols, source
        "u", "ub", "uf", "x", "ln", "ls", "lsa", "lsc",
        // Registers (display only, see _CheckArguments)
        "r", "rf", "rx", "rm",
        "vertarget",
        // Expression evaluation
        "?", "??",
        // Meta-commands
        ".frame", ".f+", ".f-", ".exr", ".ecxr", ".cxr", ".exptr", ".lastevent", ".time",
        ".echo", ".echotime", ".printf", ".formats", ".reload", ".kframes", ".effmach", ".chain", ".dumpdebug",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Extension commands (written as !name) accepted at the start of a statement.</summary>
    private static readonly FrozenSet<string> _allowedExtensionCommands = new[]
    {
        // ext / exts / uext
        "analyze", "address", "vprot", "handle", "locks", "cs", "critsec", "teb", "peb", "heap",
        "runaway", "error", "gle", "uniqstack", "findstack", "exchain", "lmi", "dh", "itoldyouso",
        "dlls", "tls",
        // SOS (read commands)
        "clrstack", "pe", "printexception", "dumpheap", "dumpobj", "do", "threads", "clrthreads",
        "eestack", "dumpstack", "syncblk", "dso", "dumpstackobjects", "ip2md", "name2ee", "dumpmt",
        "dumpclass", "dumpmd", "gcroot", "eeheap", "dumpdomain", "dumpmodule", "finalizequeue",
        "threadpool", "dumparray", "dumpvc", "objsize", "verifyheap", "dumpasync",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Extension modules that may qualify an allowlisted extension command (!sos.clrstack).</summary>
    private static readonly FrozenSet<string> _allowedExtensionModules = new[]
    {
        "ext", "exts", "uext", "ntsdexts", "sos",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Meta-commands refused anywhere in the line: they act outside the dump (processes, files,
    /// extensions, debugger paths, remoting), end the session, or run other command text.
    /// </summary>
    private static readonly string[] _refusedMetaCommands =
    [
        ".shell", ".load", ".loadby", ".unload", ".unloadall", ".extpath", ".setdll",
        ".create", ".createdir", ".attach", ".detach", ".kill", ".restart", ".abandon", ".opendump",
        ".writemem", ".readmem", ".dump", ".logopen", ".logappend", ".logclose", ".dumpcab",
        "lsf",
        ".server", ".servers", ".remote", ".endsrv", ".noshell",
        ".sympath", ".symfix", ".srcpath", ".srcfix", ".exepath", ".lsrcpath",
        ".cmdtree", ".wtitle", ".outmask", ".ocommand", ".pcmd", ".settings", ".symopt",
        // Commands that run nested command text
        ".block", ".if", ".elsif", ".else", ".foreach", ".for", ".while", ".do",
        ".break", ".continue", ".catch", ".leave",
        // Script providers
        ".scriptload", ".scriptrun", ".scriptunload", ".scriptlist", ".scriptproviders", ".scriptdebug",
        ".nvload", ".nvunload",
        // Extension commands that run a command per item
        "!for_each_frame", "!for_each_local", "!for_each_module", "!for_each_function",
        "!for_each_register",
    ];

    /// <summary>Statement heads refused with a specific reason (they are not on the allowlist either).</summary>
    private static readonly FrozenDictionary<string, string> _refusedStatements = new Dictionary<string, string>
    {
        ["as"] = "alias definitions are not allowed",
        ["ad"] = "alias definitions are not allowed",
        ["al"] = "alias definitions are not allowed",
        ["j"] = "commands that run other command text are not allowed",
        ["z"] = "commands that run other command text are not allowed",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Debugger data model names a dx expression may not reference: the root namespace and the host
    /// objects under it (utility, file system, state, scripts, settings).
    /// </summary>
    private static readonly string[] _refusedDataModelNames =
    [
        "Debugger", "debuggerRootNamespace", "Utility", "Control", "ExecuteCommand", "FileSystem",
        "State", "Scripts", "scriptContents", "Settings",
    ];

    // Script files: $<file, $><file, $$<file, $$><file, $$>a<file
    [GeneratedRegex(@"\${1,2}>?a?<", RegexOptions.CultureInvariant)]
    private static partial Regex _ScriptFileRegex();

    // Alias expansion: ${name}, $u0..$u9, $.u0..$.u9
    [GeneratedRegex(@"\$\{|\$\.?u\d", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex _AliasExpansionRegex();

    // A statement that quits the debugger: q, qq, qd
    [GeneratedRegex(@"^\s*q[qd]?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex _QuitRegex();

    // k, kb, kp, kP, kv, kn, kf, kc, kd and their combinations
    [GeneratedRegex(@"^k[bcdfhilmnpv]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex _StackFamilyRegex();

    // lm with option letters: lmv, lmvm, lmf, lml, ...
    [GeneratedRegex(@"^lm[a-z]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex _ModuleListFamilyRegex();

    /// <summary>
    /// Throws <see cref="ArgumentException"/> when the command is not allowed.
    /// </summary>
    /// <param name="command">The command line a client sent.</param>
    /// <param name="allowUnsafeCommands">Security:AllowUnsafeCommands: only the single-line and quit rules apply.</param>
    /// <param name="additionalAllowedCommands">Security:AllowedCommands: extra statement heads (e.g. "bl", ".lines", "!mex.foo").</param>
    public static void Validate(string command, bool allowUnsafeCommands, IEnumerable<string>? additionalAllowedCommands = null)
    {
        if (string.IsNullOrWhiteSpace(command))
            throw new ArgumentException("Command is required", nameof(command));

        if (command.Length > MaxCommandLength)
            throw new ArgumentException($"Command exceeds {MaxCommandLength} characters", nameof(command));

        // One command line per request: a line break would add further debugger input lines
        // (and break the output markers).
        if (command.Any(c => char.IsControl(c) && c != '\t'))
            throw new ArgumentException("Command must be a single line without control characters", nameof(command));

        foreach (var statement in command.Split(';'))
        {
            if (_QuitRegex().IsMatch(statement))
                throw new ArgumentException("Quitting the debugger is not allowed; use close_session instead", nameof(command));
        }

        if (allowUnsafeCommands)
            return;

        var refusal = FindRefusal(command, additionalAllowedCommands);
        if (refusal != null)
            throw new ArgumentException(refusal, nameof(command));
    }

    /// <summary>
    /// Returns why the command line is refused by the allowlist, naming the refused command, or null
    /// when every statement starts with an allowed command and no refused syntax appears in the line.
    /// </summary>
    public static string? FindRefusal(string command, IEnumerable<string>? additionalAllowedCommands = null)
    {
        var lineRefusal = _FindRefusedSyntax(command);
        if (lineRefusal != null)
            return lineRefusal;

        if (!TrySplitStatements(command, out var statements))
            return "Command has unbalanced quotation marks";

        if (statements.Count == 0)
            return "Command is required";

        var extra = _Normalize(additionalAllowedCommands);
        foreach (var statement in statements)
        {
            var refusal = _CheckStatement(statement, extra);
            if (refusal != null)
                return refusal;
        }

        return null;
    }

    /// <summary>
    /// Splits a command line into statements on ';' outside double quotes, dropping empty statements.
    /// Returns false when a quotation mark is left open.
    /// </summary>
    public static bool TrySplitStatements(string command, out IReadOnlyList<string> statements)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var c in command)
        {
            if (c == '"')
                inQuotes = !inQuotes;

            if (c == ';' && !inQuotes)
            {
                _AddStatement(result, current);
                continue;
            }

            current.Append(c);
        }

        _AddStatement(result, current);
        statements = result;
        return !inQuotes;
    }

    /// <summary>
    /// Returns the command a statement starts with, as the policy reads it: "kb", ".frame", "!analyze",
    /// "!sos.clrstack", "~", "|", "?" or "??"; otherwise the first word.
    /// </summary>
    public static string GetCommandName(string statement)
    {
        var text = statement.TrimStart();
        if (text.Length == 0)
            return string.Empty;

        switch (text[0])
        {
            case '?':
                return text.StartsWith("??", StringComparison.Ordinal) ? "??" : "?";
            case '~':
            case '|':
                return text[0].ToString();
            case '!':
                return "!" + _TakeWhile(text, 1, c => char.IsLetterOrDigit(c) || c is '_' or '.');
            case '.':
                var name = "." + _TakeWhile(text, 1, c => char.IsLetterOrDigit(c) || c == '_');
                if (name.Equals(".f", StringComparison.OrdinalIgnoreCase) && text.Length > 2 && text[2] is '+' or '-')
                    return name + text[2];
                return name;
        }

        if (char.IsAsciiLetter(text[0]))
            return _TakeWhile(text, 0, char.IsAsciiLetter);

        return _TakeWhile(text, 0, c => !char.IsWhiteSpace(c));
    }

    /// <summary>True when <paramref name="name"/> (as returned by <see cref="GetCommandName"/>) is on the built-in allowlist.</summary>
    public static bool IsAllowedCommand(string name)
    {
        if (name.Length == 0)
            return false;

        if (name[0] == '!')
            return _IsAllowedExtension(name);

        return _allowedCommands.Contains(name) || _IsStandardFamily(name);
    }

    private static string? _FindRefusedSyntax(string command)
    {
        if (_ScriptFileRegex().IsMatch(command))
            return $"Command '$<' is not allowed by the server's command policy: script files are not allowed";

        if (_AliasExpansionRegex().IsMatch(command))
            return $"Command '{_AliasExpansionRegex().Match(command).Value}' is not allowed by the server's command policy: alias expansion is not allowed";

        var meta = _FindToken(command, _refusedMetaCommands);
        if (meta != null)
            return $"Command '{meta}' is not allowed by the server's command policy (Security:AllowUnsafeCommands=false)";

        return null;
    }

    private static string? _CheckStatement(string statement, FrozenSet<string> extra)
    {
        var text = statement.Trim();
        var name = GetCommandName(text);

        // ';' inside quotes is documented as literal text only for .echo
        if (text.Contains(';') && !name.Equals(".echo", StringComparison.OrdinalIgnoreCase))
            return $"Command '{name}' is not allowed by the server's command policy: ';' inside quotes is accepted only for .echo";

        if (name == "~")
            return _CheckThreadCommand(text, extra);

        if (name == "|")
            return _CheckProcessCommand(text);

        if (_refusedStatements.TryGetValue(name, out var reason))
            return $"Command '{name}' is not allowed by the server's command policy: {reason}";

        if (extra.Contains(name))
            return null;

        if (!IsAllowedCommand(name))
            return $"Command '{name}' is not allowed by the server's command policy: {AllowedCommandsHint}";

        return _CheckArguments(name, text[name.Length..]);
    }

    /// <summary>
    /// ~ [thread] [s | command]: thread listing and switching, or an allowlisted command run on the
    /// listed threads (~* k). ~e (run command text per thread) is refused.
    /// </summary>
    private static string? _CheckThreadCommand(string text, FrozenSet<string> extra)
    {
        var rest = _SkipSpecifier(text, 1);
        if (rest == null)
            return $"Command '~' is not allowed by the server's command policy: malformed thread specifier";

        var trimmed = rest.Trim();
        if (trimmed.Length == 0 || trimmed.Equals("s", StringComparison.OrdinalIgnoreCase))
            return null;

        if (trimmed[0] is 'e' or 'E' && (trimmed.Length == 1 || !char.IsAsciiLetter(trimmed[1])))
            return $"Command '~e' is not allowed by the server's command policy: commands that run other command text are not allowed";

        return _CheckStatement(trimmed, extra);
    }

    /// <summary>| [process] [s]: process listing and switching only.</summary>
    private static string? _CheckProcessCommand(string text)
    {
        var start = text.StartsWith("||", StringComparison.Ordinal) ? 2 : 1;
        var rest = _SkipSpecifier(text, start);
        var trimmed = rest?.Trim();
        if (trimmed is not null && (trimmed.Length == 0 || trimmed.Equals("s", StringComparison.OrdinalIgnoreCase)))
            return null;

        return $"Command '|' is not allowed by the server's command policy: only process listing and switching are accepted";
    }

    /// <summary>
    /// Skips a thread or process specifier after ~ or |: *, ., #, a number, [expression] or ~[id].
    /// Returns the remaining text, or null when a bracket is left open.
    /// </summary>
    private static string? _SkipSpecifier(string text, int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
            index++;

        if (index < text.Length && text[index] == '~')
            index++;

        if (index < text.Length && text[index] == '[')
        {
            var close = text.IndexOf(']', index);
            if (close < 0)
                return null;
            return text[(close + 1)..];
        }

        if (index < text.Length && text[index] is '*' or '.' or '#')
            return text[(index + 1)..];

        while (index < text.Length && char.IsAsciiDigit(text[index]))
            index++;

        return text[index..];
    }

    /// <summary>Argument rules for allowlisted commands that also have writing or loading forms.</summary>
    private static string? _CheckArguments(string name, string arguments)
    {
        switch (name.ToLowerInvariant())
        {
            case "r" or "rf" or "rx" or "rm" when arguments.Contains('='):
                return $"Command '{name}' is not allowed by the server's command policy: registers can be displayed, not changed";

            case ".reload" when _HasOption(arguments, "/o"):
                return $"Command '.reload /o' is not allowed by the server's command policy: it overwrites files in the symbol store";

            case "dx":
                var dataModelName = _FindToken(arguments, _refusedDataModelNames, dataModel: true);
                if (dataModelName != null)
                    return $"Command 'dx' is not allowed by the server's command policy: the expression may not reference '{dataModelName}'";
                return null;
        }

        var extension = name.StartsWith('!') ? name[(name.LastIndexOf('.') + 1)..].TrimStart('!').ToLowerInvariant() : null;
        switch (extension)
        {
            case "analyze":
                foreach (var option in _Options(arguments))
                {
                    if (!_IsAllowedAnalyzeOption(option))
                        return $"Command '!analyze {option}' is not allowed by the server's command policy: only -v, -f, -hang and -show are accepted";
                }
                return null;

            case "heap" when _HasOption(arguments, "-b"):
                return $"Command '!heap -b' is not allowed by the server's command policy: heap breakpoints are not accepted";
        }

        return null;
    }

    private static bool _IsAllowedAnalyzeOption(string option) =>
        option.Equals("-f", StringComparison.OrdinalIgnoreCase)
        || option.Equals("-hang", StringComparison.OrdinalIgnoreCase)
        || option.Equals("-show", StringComparison.OrdinalIgnoreCase)
        || (option.StartsWith("-v", StringComparison.OrdinalIgnoreCase) && option[2..].All(char.IsAsciiDigit));

    private static IEnumerable<string> _Options(string arguments) =>
        arguments.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).Where(a => a[0] is '-' or '/');

    private static bool _HasOption(string arguments, string option) =>
        _Options(arguments).Any(o => o.Equals(option, StringComparison.OrdinalIgnoreCase));

    private static bool _IsStandardFamily(string name) =>
        _StackFamilyRegex().IsMatch(name) || _ModuleListFamilyRegex().IsMatch(name);

    private static bool _IsAllowedExtension(string name)
    {
        var body = name[1..];
        var dot = body.IndexOf('.');
        if (dot < 0)
            return _allowedExtensionCommands.Contains(body);

        // !module.command: a known extension module, one level only
        var module = body[..dot];
        var command = body[(dot + 1)..];
        return _allowedExtensionModules.Contains(module) && _allowedExtensionCommands.Contains(command);
    }

    /// <summary>
    /// Returns the first of <paramref name="tokens"/> found as a whole token anywhere in the text
    /// (case-insensitive), or null. With <paramref name="dataModel"/> the tokens are data model
    /// identifiers, so '.' and '@$' separate rather than join.
    /// </summary>
    private static string? _FindToken(string text, IEnumerable<string> tokens, bool dataModel = false)
    {
        Func<char, bool> isTokenChar = dataModel
            ? c => char.IsLetterOrDigit(c) || c == '_'
            : c => char.IsLetterOrDigit(c) || c is '_' or '.' or '!';

        foreach (var token in tokens)
        {
            var index = 0;
            while ((index = text.IndexOf(token, index, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                var end = index + token.Length;
                var startsToken = index == 0 || !isTokenChar(text[index - 1]);
                var endsToken = end == text.Length || !isTokenChar(text[end]);
                if (startsToken && endsToken)
                    return token;
                index = end;
            }
        }

        return null;
    }

    private static FrozenSet<string> _Normalize(IEnumerable<string>? names) =>
        (names ?? []).Select(n => n.Trim()).Where(n => n.Length > 0).ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static string _TakeWhile(string text, int start, Func<char, bool> predicate)
    {
        var end = start;
        while (end < text.Length && predicate(text[end]))
            end++;
        return text[start..end];
    }

    private static void _AddStatement(List<string> statements, StringBuilder current)
    {
        var statement = current.ToString();
        if (!string.IsNullOrWhiteSpace(statement))
            statements.Add(statement);
        current.Clear();
    }
}
