using DumpAnalysisService.Security;
using FsCheck;
using FsCheck.Xunit;
using Shared.Configuration;

namespace DumpAnalysisService.Tests.Security;

/// <summary>
/// Property tests (FsCheck) for the validators that stand between client input and cdb.exe: for any
/// input they either accept it or throw <see cref="ArgumentException"/>, and what they accept keeps
/// the invariants the command line and the symbol path rely on.
/// </summary>
public sealed class InputValidationPropertyTests
{
    // A sample of what the policy refuses anywhere in the line, written as a client would: any case, mid-line.
    private static readonly string[] RefusedSyntax =
    [
        ".shell", ".load", ".loadby", ".create", ".attach", ".writemem", ".dump", ".logopen",
        ".sympath", ".server", "lsf", "$<", "$$><", "$$>a<",
        ".scriptload", ".scriptrun", ".nvload", ".block", ".if", ".foreach", ".while", "!for_each_frame",
        "${", "$u0", "$.u3",
    ];

    // Heads the policy accepts, used to build commands that get past the first check.
    private static readonly string[] AllowedHeads =
    [
        "kb", "~*", "~0", "dx", "dps", "u", "x", "lm", "r", "?", "??", ".echo", ".frame", ".reload",
        "!analyze", "!heap", "!sos.clrstack", "|",
    ];

    [Property(MaxTest = 500)]
    public void CommandPolicy_AcceptsOrThrowsArgumentException(NonNull<string> command, bool allowUnsafeCommands)
    {
        try
        {
            CdbCommandPolicy.Validate(command.Get, allowUnsafeCommands);
        }
        catch (ArgumentException)
        {
        }
    }

    [Property(MaxTest = 500)]
    public void CommandPolicy_RejectsRefusedSyntaxAnywhereInTheLine(NonNull<string> before, NonNull<string> after, byte pick, bool upper)
    {
        var unsafeCommand = RefusedSyntax[pick % RefusedSyntax.Length];
        if (upper && !unsafeCommand.StartsWith('$'))
            unsafeCommand = unsafeCommand.ToUpperInvariant();

        var command = $"{before.Get}; {unsafeCommand} {after.Get}";

        Assert.Throws<ArgumentException>(() => CdbCommandPolicy.Validate(command, allowUnsafeCommands: false));
    }

    [Property(MaxTest = 500)]
    public void CommandPolicy_RejectsControlCharactersEvenWhenUnsafeCommandsAreAllowed(NonNull<string> before, NonNull<string> after, byte pick)
    {
        // Every C0 control character except tab, plus DEL and NEL: a line break would inject a second debugger input line.
        char[] controls = [.. Enumerable.Range(0, 32).Where(c => c != '\t').Select(c => (char)c), '\u007f', '\u0085'];
        var command = $"kb {before.Get}{controls[pick % controls.Length]}{after.Get}";

        Assert.Throws<ArgumentException>(() => CdbCommandPolicy.Validate(command, allowUnsafeCommands: true));
    }

    [Property(MaxTest = 500)]
    public void CommandPolicy_AcceptedCommandIsOneSafeLine(NonNull<string> command)
    {
        _AssertAcceptedCommandKeepsInvariants(command.Get);
    }

    [Property(MaxTest = 2000)]
    public void CommandPolicy_AcceptedCommandStartsEveryStatementWithAnAllowedCommand(byte pick, NonNull<string> arguments, byte secondPick, NonNull<string> secondArguments)
    {
        var command = $"{AllowedHeads[pick % AllowedHeads.Length]} {arguments.Get}; {AllowedHeads[secondPick % AllowedHeads.Length]}{secondArguments.Get}";

        _AssertAcceptedCommandKeepsInvariants(command);
    }

    private static void _AssertAcceptedCommandKeepsInvariants(string command)
    {
        try
        {
            CdbCommandPolicy.Validate(command, allowUnsafeCommands: false);
        }
        catch (ArgumentException)
        {
            return;
        }

        Assert.InRange(command.Length, 1, CdbCommandPolicy.MaxCommandLength);
        Assert.DoesNotContain(command, c => char.IsControl(c) && c != '\t');
        Assert.Null(CdbCommandPolicy.FindRefusal(command));

        // No alias expansion or script files anywhere in the line, quoted text included.
        Assert.DoesNotContain("${", command);
        Assert.DoesNotMatch(@"(?i)\$\.?u\d", command);
        Assert.DoesNotMatch(@"\${1,2}>?a?<", command);

        Assert.True(CdbCommandPolicy.TrySplitStatements(command, out var statements));
        Assert.NotEmpty(statements);
        foreach (var statement in statements)
        {
            var name = CdbCommandPolicy.GetCommandName(statement);
            Assert.True(name is "~" or "|" || CdbCommandPolicy.IsAllowedCommand(name), $"'{name}' is not an allowed command");
            Assert.False(name.Equals("dx", StringComparison.OrdinalIgnoreCase) && statement.Contains("Debugger", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Property(MaxTest = 500)]
    public void SymbolInput_AcceptedValuesCannotEscapeTheSymbolPathArgument(string? cache, string? pathExtra, string? servers)
    {
        try
        {
            SymbolInputValidator.Validate(new SymbolsConfiguration(cache, pathExtra, servers));
        }
        catch (ArgumentException)
        {
            return;
        }

        foreach (var value in new[] { cache, pathExtra, servers })
        {
            if (string.IsNullOrEmpty(value))
                continue;
            Assert.True(value.Length <= SymbolInputValidator.MaxLength);
            Assert.DoesNotContain(value, c => c == '"' || char.IsControl(c));
        }

        // The cache is a single directory: it may not add further symbol path elements.
        if (!string.IsNullOrEmpty(cache))
            Assert.DoesNotContain(cache, c => c is ';' or '*');
    }
}
