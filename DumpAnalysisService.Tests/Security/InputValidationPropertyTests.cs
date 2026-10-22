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
    // A sample of what the policy blocks, written as a client would: any case, mid-line.
    private static readonly string[] UnsafeCommands =
    [
        ".shell", ".load", ".loadby", ".create", ".attach", ".writemem", ".dump", ".logopen",
        ".sympath", ".server", "lsf", "$<", "$$><", "$$>a<",
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
    public void CommandPolicy_RejectsUnsafeCommandAnywhereInTheLine(NonNull<string> before, NonNull<string> after, byte pick, bool upper)
    {
        var unsafeCommand = UnsafeCommands[pick % UnsafeCommands.Length];
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
        try
        {
            CdbCommandPolicy.Validate(command.Get, allowUnsafeCommands: false);
        }
        catch (ArgumentException)
        {
            return;
        }

        Assert.InRange(command.Get.Length, 1, CdbCommandPolicy.MaxCommandLength);
        Assert.DoesNotContain(command.Get, c => char.IsControl(c) && c != '\t');
        Assert.Null(CdbCommandPolicy.FindUnsafeToken(command.Get));
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
