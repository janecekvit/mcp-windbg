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
}
