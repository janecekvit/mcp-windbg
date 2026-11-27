using DumpAnalysisService.Infrastructure.Debugger;

namespace DumpAnalysisService.Tests.Infrastructure;

public class CdbOutputCollectorTests
{
    private const string Start = "__START_COMMAND_abc__";
    private const string End = "__END_COMMAND_abc__";

    [Fact]
    public void Append_FullOutputInOneChunk_ReturnsOutputWithoutPrompts()
    {
        var collector = new CdbOutputCollector(Start, End);

        var done = collector.Append($"0:000> {Start}\n0:000> line 1\nline 2\n0:000> {End}\n0:000> ");

        Assert.True(done);
        Assert.Equal("line 1\nline 2", collector.Result);
    }

    [Fact]
    public void Append_DiscardsStaleOutputBeforeStartMarker()
    {
        var collector = new CdbOutputCollector(Start, End);

        collector.Append("late output of a command that timed out\n__END_COMMAND_old__\n");
        var done = collector.Append($"0:000> {Start}\n0:000> fresh\n0:000> {End}\n");

        Assert.True(done);
        Assert.Equal("fresh", collector.Result);
    }

    [Fact]
    public void Append_MarkersSplitAcrossChunks_AreFound()
    {
        var collector = new CdbOutputCollector(Start, End);
        var stream = $"noise 0:000> {Start}\n0:000> result text\n0:000> {End}\n";

        var done = false;
        foreach (var chunk in stream.Chunk(3))
        {
            done = collector.Append(new string(chunk));
            if (done)
                break;
        }

        Assert.True(done);
        Assert.Equal("result text", collector.Result);
    }

    [Fact]
    public void Append_WithoutEndMarker_IsNotComplete()
    {
        var collector = new CdbOutputCollector(Start, End);

        Assert.False(collector.Append($"{Start}\npartial output"));
        Assert.False(collector.IsComplete);
    }

    [Fact]
    public void Append_KernelPrompt_IsStripped()
    {
        var collector = new CdbOutputCollector(Start, End);

        collector.Append($"0: kd> {Start}\n0: kd> bugcheck info\n0: kd> {End}\n");

        Assert.Equal("bugcheck info", collector.Result);
    }

    [Fact]
    public void Append_EmptyOutput_ReturnsEmptyString()
    {
        var collector = new CdbOutputCollector(Start, End);

        Assert.True(collector.Append($"0:000> {Start}\n0:000> 0:000> {End}\n"));
        Assert.Equal(string.Empty, collector.Result);
    }

    [Fact]
    public void Append_LargeOutput_CompletesOnce()
    {
        var collector = new CdbOutputCollector(Start, End);
        collector.Append($"{Start}\n");

        for (var i = 0; i < 10_000; i++)
            Assert.False(collector.Append($"frame {i}\n"));

        Assert.True(collector.Append($"0:000> {End}\n"));
        Assert.StartsWith("frame 0", collector.Result);
        Assert.EndsWith("frame 9999", collector.Result);
    }

    [Fact]
    public void Append_DropsProgressOnlyLines()
    {
        var collector = new CdbOutputCollector(Start, End);

        collector.Append($"{Start}\r\nMapping file section regions...\r\nMapping module regions...\r\n" +
            $"Mapping heap regions...\r\n  BaseAddress      Usage\r\n..........\r\nLoading User Symbols\r\n  7ff0000 Image\r\n{End}\r\n");

        Assert.Equal("BaseAddress      Usage\r\n  7ff0000 Image", collector.Result);
    }

    [Fact]
    public void Append_KeepsLinesThatOnlyMentionProgressWords()
    {
        var collector = new CdbOutputCollector(Start, End);

        collector.Append($"{Start}\nmodule!Mapping regions... failed at 0x10\nwait...\n{End}\n");

        Assert.Equal("module!Mapping regions... failed at 0x10\nwait...", collector.Result);
    }

    [Fact]
    public void Append_OutputOverTheLimit_IsTruncatedWithANote()
    {
        var collector = new CdbOutputCollector(Start, End, maxOutputBytes: 10);

        collector.Append($"{Start}\n0123456789abcdefghij\n{End}\n");

        Assert.StartsWith("0123456789", collector.Result);
        Assert.EndsWith("[output truncated: 10 bytes omitted]", collector.Result);
    }

    [Fact]
    public void Append_OutputWithinTheLimit_HasNoNote()
    {
        var collector = new CdbOutputCollector(Start, End, maxOutputBytes: 100);

        collector.Append($"{Start}\nshort\n{End}\n");

        Assert.Equal("short", collector.Result);
    }

    [Fact]
    public void Append_TruncationCountsUtf8BytesAndKeepsWholeCharacters()
    {
        var collector = new CdbOutputCollector(Start, End, maxOutputBytes: 5);

        // "šš" is 4 bytes, the third "š" would be bytes 5-6.
        collector.Append($"{Start}\nššš\n{End}\n");

        Assert.StartsWith("šš" + Environment.NewLine, collector.Result);
        Assert.EndsWith("[output truncated: 2 bytes omitted]", collector.Result);
    }

    [Fact]
    public void Append_LongOutputInManyChunks_StaysBoundedAndFindsTheEndMarker()
    {
        var collector = new CdbOutputCollector(Start, End, maxOutputBytes: 1000);
        collector.Append($"{Start}\n");

        var line = new string('x', 99) + "\n";
        for (var i = 0; i < 10_000; i++)
            Assert.False(collector.Append(line));

        // The END marker split across two chunks after a megabyte of output.
        Assert.False(collector.Append(End[..5]));
        Assert.True(collector.Append(End[5..] + "\n"));

        Assert.StartsWith(new string('x', 99), collector.Result);
        // About everything beyond the first 1000 bytes; the trailing line break is trimmed, not counted.
        var omitted = long.Parse(System.Text.RegularExpressions.Regex.Match(collector.Result, @"\[output truncated: (\d+) bytes omitted\]$").Groups[1].Value);
        Assert.InRange(omitted, 10_000 * 100 - 1000 - 1, 10_000 * 100 - 1000);
        Assert.True(collector.Result.Length < 1100);
    }

    [Fact]
    public void Append_ZeroLimit_IsUnbounded()
    {
        var collector = new CdbOutputCollector(Start, End, maxOutputBytes: 0);
        var output = new string('y', 300_000);

        collector.Append($"{Start}\n{output}\n{End}\n");

        Assert.Equal(output, collector.Result);
    }
}

