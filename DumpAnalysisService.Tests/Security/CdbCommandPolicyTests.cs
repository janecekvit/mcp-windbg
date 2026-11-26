using DumpAnalysisService.Security;

namespace DumpAnalysisService.Tests.Security;

public class CdbCommandPolicyTests
{
    private static void _AssertAllowed(string command, IEnumerable<string>? extra = null) =>
        CdbCommandPolicy.Validate(command, allowUnsafeCommands: false, extra);

    private static ArgumentException _AssertRejected(string command, IEnumerable<string>? extra = null) =>
        Assert.Throws<ArgumentException>(() => CdbCommandPolicy.Validate(command, allowUnsafeCommands: false, extra));

    [Theory]
    // Stacks and threads
    [InlineData("k")]
    [InlineData("kb")]
    [InlineData("kn 40")]
    [InlineData("kPn")]
    [InlineData("~")]
    [InlineData("~*")]
    [InlineData("~* kb")]
    [InlineData("~*k")]
    [InlineData("~0s")]
    [InlineData("~3 kv")]
    [InlineData("~~[0x1a2b]s")]
    [InlineData("~[@$t0]s")]
    [InlineData("|")]
    [InlineData("|0s")]
    [InlineData(".frame 2")]
    [InlineData(".frame /r 2")]
    [InlineData(".f+")]
    [InlineData(".f-")]
    [InlineData(".kframes 100")]
    // Exception and event context
    [InlineData(".exr -1")]
    [InlineData(".ecxr")]
    [InlineData(".ecxr; kn 40")]
    [InlineData(".cxr 0x1000")]
    [InlineData(".lastevent")]
    [InlineData(".time")]
    [InlineData("vertarget")]
    // Memory, types and disassembly
    [InlineData("dps @rsp L20")]
    [InlineData("dqs@rsp")]
    [InlineData("da 0x1000")]
    [InlineData("du @rcx")]
    [InlineData("dt ntdll!_PEB @$peb")]
    [InlineData("dv /t")]
    [InlineData("s -a 0 L?1000 \"placeholder\"")]
    [InlineData("u @rip")]
    [InlineData("ub @rip L10")]
    [InlineData("uf module!function")]
    // Symbols and modules
    [InlineData("x module!*")]
    [InlineData("x falsfoo")]
    [InlineData("ln @rip")]
    [InlineData("lm")]
    [InlineData("lmvm module")]
    [InlineData("lm v m module")]
    [InlineData(".reload")]
    [InlineData(".reload /f module.dll")]
    [InlineData("ls")]
    // Registers and expressions
    [InlineData("r")]
    [InlineData("r rax, rbx")]
    [InlineData("? 1 + 2")]
    [InlineData("?? sizeof(int)")]
    [InlineData(".formats 0x10")]
    [InlineData(".echo hello")]
    [InlineData(".echo \"semi; colon\"")]
    [InlineData(".printf \"%p\\n\", @rip")]
    // Extensions
    [InlineData("!analyze -v")]
    [InlineData("!analyze -v -hang")]
    [InlineData("!address -summary")]
    [InlineData("!heap -s")]
    [InlineData("!handle 0 f")]
    [InlineData("!locks")]
    [InlineData("!teb")]
    [InlineData("!peb")]
    [InlineData("!runaway 7")]
    [InlineData("!gle")]
    [InlineData("!uniqstack")]
    [InlineData("!lmi module")]
    [InlineData("!clrstack")]
    [InlineData("!sos.clrstack -a")]
    [InlineData("!DumpHeap -stat")]
    [InlineData("!do 0x1000")]
    // Data model
    [InlineData("dx @$curprocess.Threads")]
    [InlineData("dx -r2 @$curthread.Stack.Frames")]
    [InlineData("dx @$cursession.Processes.Select(p => p.Name)")]
    // Several statements
    [InlineData("kb; lm; !peb")]
    [InlineData("kb;")]
    public void Validate_ReadOnlyCommands_AreAllowed(string command)
    {
        _AssertAllowed(command);
    }

    [Theory]
    [InlineData("as name value")]
    [InlineData("aS name value")]
    [InlineData("ad name")]
    [InlineData("al")]
    [InlineData("kb; as name value")]
    [InlineData("~* as name value")]
    [InlineData("${name}")]
    [InlineData("kb ${name}")]
    [InlineData(".echo ${name}")]
    [InlineData("$u0")]
    [InlineData("? $u5")]
    [InlineData("? $.u1")]
    [InlineData("dd $U9")]
    public void Rejects_AliasDefinitionAndExpansion(string command)
    {
        _AssertRejected(command);
    }

    [Theory]
    [InlineData(".block { kb }")]
    [InlineData(".if (1) { kb }")]
    [InlineData(".if (1) { kb } .else { lm }")]
    [InlineData(".foreach (t { lm }) { .echo t }")]
    [InlineData(".for (r $t0 = 0; @$t0 < 2; r $t0 = @$t0 + 1) { kb }")]
    [InlineData(".while (1) { kb }")]
    [InlineData(".do { kb } (0)")]
    [InlineData("j 1 'kb' ; 'lm'")]
    [InlineData("z (0) kb")]
    [InlineData("~e kb")]
    [InlineData("~*e kb")]
    [InlineData("~0 e kb")]
    [InlineData("!for_each_frame kb")]
    [InlineData("!for_each_module .echo x")]
    [InlineData(".pcmd -s kb")]
    [InlineData(".ocommand prefix")]
    [InlineData("kb; .echo .block")]
    public void Rejects_NestedCommandExecution(string command)
    {
        _AssertRejected(command);
    }

    [Theory]
    [InlineData("$<placeholder")]
    [InlineData("$><placeholder")]
    [InlineData("$$<placeholder")]
    [InlineData("$$><placeholder")]
    [InlineData("$$>a<placeholder arg")]
    [InlineData("kb; $$>a<placeholder")]
    public void Rejects_ScriptFiles(string command)
    {
        _AssertRejected(command);
    }

    [Theory]
    [InlineData(".scriptload placeholder")]
    [InlineData(".scriptrun placeholder")]
    [InlineData(".scriptunload placeholder")]
    [InlineData(".SCRIPTLOAD placeholder")]
    [InlineData(".nvload placeholder")]
    [InlineData("kb; .scriptlist")]
    public void Rejects_ScriptLoading(string command)
    {
        _AssertRejected(command);
    }

    [Theory]
    [InlineData("dx Debugger.Utility")]
    [InlineData("dx debugger.utility.control")]
    [InlineData("dx Debugger . Utility")]
    [InlineData("dx Debugger.State")]
    [InlineData("dx Debugger.Settings")]
    [InlineData("dx Debugger.Sessions[0]")]
    [InlineData("dx @$scripts")]
    [InlineData("dx @ $scripts")]
    [InlineData("dx @$debuggerRootNamespace")]
    [InlineData("dx @$curprocess.Utility")]
    [InlineData("dx -r1 @$x.FileSystem")]
    [InlineData("dx @$x.ExecuteCommand(\"placeholder\")")]
    [InlineData("~* dx Debugger.Utility")]
    public void Rejects_DxHostObjectNamespaces(string command)
    {
        _AssertRejected(command);
    }

    [Theory]
    [InlineData(".shell placeholder")]
    [InlineData(".SHELL placeholder")]
    [InlineData(".load placeholder")]
    [InlineData(".loadby placeholder placeholder")]
    [InlineData(".create placeholder")]
    [InlineData(".attach 0n1")]
    [InlineData(".writemem placeholder 0 L10")]
    [InlineData(".readmem placeholder 0 L10")]
    [InlineData(".dump /ma placeholder")]
    [InlineData(".logopen placeholder")]
    [InlineData(".sympath+ placeholder")]
    [InlineData(".symopt+ 0x1")]
    [InlineData(".server placeholder")]
    [InlineData(".settings load placeholder")]
    [InlineData("lsf placeholder")]
    [InlineData("kb; .shell placeholder")]
    [InlineData(".echo \"text .shell\"")]
    public void Rejects_MetaCommandsActingOutsideTheDump(string command)
    {
        _AssertRejected(command);
    }

    [Theory]
    [InlineData("e 0x1000 0")]
    [InlineData("eb 0x1000 0")]
    [InlineData("f 0x1000 L10 0")]
    [InlineData("bp module!function")]
    [InlineData("g")]
    [InlineData("sxe -c \"kb\" av")]
    [InlineData("r rax=0")]
    [InlineData("r @$t0 = 1")]
    [InlineData(".reload /o")]
    [InlineData("!analyze -xmlf placeholder")]
    [InlineData("!analyze -c -load placeholder")]
    [InlineData("!heap -b alloc")]
    [InlineData("!placeholder")]
    [InlineData("!other.analyze")]
    [InlineData("!")]
    [InlineData(".tlist")]
    [InlineData("|*e kb")]
    [InlineData("0x1000")]
    [InlineData("$$ comment")]
    [InlineData("dtmodule!_PEB")]
    [InlineData(".printf \"semi;colon\"")]
    [InlineData(".echo \"unbalanced")]
    [InlineData("~[0x1 s")]
    public void Rejects_CommandsOutsideTheAllowlist(string command)
    {
        _AssertRejected(command);
    }

    [Fact]
    public void Rejection_NamesTheRefusedCommand()
    {
        var ex = _AssertRejected("kb; bp placeholder");

        Assert.Contains("'bp'", ex.Message);
        Assert.Contains("Security:AllowedCommands", ex.Message);
    }

    [Theory]
    [InlineData("bl", "bl")]
    [InlineData(".lines", ".lines")]
    [InlineData("!placeholder.command", "!placeholder.command -a")]
    [InlineData("BL", "bl")]
    [InlineData("bl", "~* bl")]
    public void AdditionalAllowedCommands_AreAccepted(string allowed, string command)
    {
        _AssertRejected(command);
        _AssertAllowed(command, [allowed]);
    }

    [Theory]
    [InlineData("as", "as name value")]
    [InlineData(".block", ".block { kb }")]
    [InlineData(".scriptload", ".scriptload placeholder")]
    [InlineData(".shell", ".shell placeholder")]
    [InlineData("bl", "bl; ${name}")]
    public void AdditionalAllowedCommands_DoNotLiftLineWideRefusals(string allowed, string command)
    {
        _AssertRejected(command, [allowed]);
    }

    [Theory]
    [InlineData(".load placeholder")]
    [InlineData("$<placeholder")]
    [InlineData("bp placeholder")]
    [InlineData("as name value")]
    public void Validate_UnsafeCommandsWhenAllowed_DoesNotThrow(string command)
    {
        CdbCommandPolicy.Validate(command, allowUnsafeCommands: true);
    }

    [Theory]
    [InlineData("q")]
    [InlineData("qq")]
    [InlineData("qd")]
    [InlineData("kb; q")]
    [InlineData(" Q ")]
    public void Validate_QuitCommands_ThrowEvenWhenUnsafeAllowed(string command)
    {
        Assert.Throws<ArgumentException>(() => CdbCommandPolicy.Validate(command, allowUnsafeCommands: true));
    }

    [Theory]
    [InlineData("kb\nlm")]
    [InlineData("kb\r\nlm")]
    [InlineData("kb\0")]
    public void Validate_ControlCharacters_ThrowEvenWhenUnsafeAllowed(string command)
    {
        Assert.Throws<ArgumentException>(() => CdbCommandPolicy.Validate(command, allowUnsafeCommands: true));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(";")]
    public void Validate_EmptyCommand_Throws(string command)
    {
        Assert.Throws<ArgumentException>(() => CdbCommandPolicy.Validate(command, allowUnsafeCommands: false));
    }

    [Fact]
    public void Validate_TooLongCommand_Throws()
    {
        var command = "kb " + new string('a', CdbCommandPolicy.MaxCommandLength);
        Assert.Throws<ArgumentException>(() => CdbCommandPolicy.Validate(command, allowUnsafeCommands: false));
    }

    [Theory]
    [InlineData("kb; lm", new[] { "kb", " lm" })]
    [InlineData(".echo \"a; b\"; kb", new[] { ".echo \"a; b\"", " kb" })]
    [InlineData("kb;;lm;", new[] { "kb", "lm" })]
    public void TrySplitStatements_SplitsOutsideQuotes(string command, string[] expected)
    {
        Assert.True(CdbCommandPolicy.TrySplitStatements(command, out var statements));
        Assert.Equal(expected, statements);
    }

    [Theory]
    [InlineData("kb 10", "kb")]
    [InlineData("  dqs@rsp", "dqs")]
    [InlineData(".f+", ".f+")]
    [InlineData(".frame 1", ".frame")]
    [InlineData("!sos.clrstack", "!sos.clrstack")]
    [InlineData("?? 1", "??")]
    [InlineData("~* kb", "~")]
    [InlineData("$$>a<x", "$$>a<x")]
    public void GetCommandName_ReadsTheStatementHead(string statement, string expected)
    {
        Assert.Equal(expected, CdbCommandPolicy.GetCommandName(statement));
    }
}
