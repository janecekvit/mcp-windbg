using DumpAnalysisService.Security;

namespace DumpAnalysisService.Tests.Security;

public class CdbCommandPolicyTests
{
    [Theory]
    [InlineData("kb")]
    [InlineData("!analyze -v")]
    [InlineData("~* kb")]
    [InlineData("dt ntdll!_PEB @$peb")]
    [InlineData("lm v m ntdll")]
    [InlineData(".ecxr; kb")]
    [InlineData(".echo hello")]
    [InlineData(".reload")]
    [InlineData("!heap -s")]
    [InlineData(".frame 2")]
    [InlineData("dx @$curprocess.Threads")]
    [InlineData(".shellx")] // not a meta-command, only shares a prefix
    [InlineData("x falsfoo")] // contains "lsf" inside a word
    [InlineData(".foreach (t { ~* e kb }) { .echo t }")]
    [InlineData("ls")]
    public void Validate_SafeCommands_DoesNotThrow(string command)
    {
        CdbCommandPolicy.Validate(command, allowUnsafeCommands: false);
    }

    [Theory]
    [InlineData(".shell dir")]
    [InlineData(".SHELL dir")]
    [InlineData(".load ext.dll")]
    [InlineData(".loadby sos clr")]
    [InlineData(".create app.exe")]
    [InlineData(".attach 0n1234")]
    [InlineData(".writemem out.bin 0 L10")]
    [InlineData(".dump /ma out.dmp")]
    [InlineData(".logopen out.txt")]
    [InlineData("$<script.txt")]
    [InlineData("$$><script.txt")]
    [InlineData("$$>a<script.txt arg")]
    [InlineData("kb; .shell dir")]
    [InlineData(".if (1) { .load ext.dll }")]
    [InlineData(".sympath+ C:\\other")]
    [InlineData(".server tcp:port=5005")]
    [InlineData("lsf C:\\file.txt")]
    [InlineData(".readmem in.bin 0 L10")]
    [InlineData(".foreach /f (t \"C:\\list.txt\") { .echo t }")]
    [InlineData(".foreach /pS 1 /f (t \"C:\\list.txt\") { .echo t }")]
    public void Validate_UnsafeCommands_Throws(string command)
    {
        var ex = Assert.Throws<ArgumentException>(() => CdbCommandPolicy.Validate(command, allowUnsafeCommands: false));
        Assert.Contains("blocked", ex.Message);
    }

    [Theory]
    [InlineData(".load ext.dll")]
    [InlineData("$<script.txt")]
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
    public void Validate_EmptyCommand_Throws(string command)
    {
        Assert.Throws<ArgumentException>(() => CdbCommandPolicy.Validate(command, allowUnsafeCommands: false));
    }

    [Fact]
    public void Validate_TooLongCommand_Throws()
    {
        var command = new string('a', CdbCommandPolicy.MaxCommandLength + 1);
        Assert.Throws<ArgumentException>(() => CdbCommandPolicy.Validate(command, allowUnsafeCommands: false));
    }
}
