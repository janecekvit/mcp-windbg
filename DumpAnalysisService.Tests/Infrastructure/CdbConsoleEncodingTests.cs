using System.Text;
using DumpAnalysisService.Infrastructure.Debugger;

namespace DumpAnalysisService.Tests.Infrastructure;

public class CdbConsoleEncodingTests
{
    [Theory]
    [InlineData(1250, new byte[] { 0x9A }, "š")] // Central European
    [InlineData(1252, new byte[] { 0x9A }, "š")] // Western European
    [InlineData(65001, new byte[] { 0xC5, 0xA1 }, "š")]
    public void ForCodePage_DecodesWhatCdbWritesInThatCodePage(int codePage, byte[] bytes, string expected)
    {
        var encoding = CdbConsoleEncoding.ForCodePage(codePage);

        Assert.Equal(expected, encoding.GetString(bytes));
        Assert.Equal(bytes, encoding.GetBytes(expected));
    }

    [Fact]
    public void ForCodePage_Utf8_WritesNoByteOrderMark()
    {
        Assert.Empty(CdbConsoleEncoding.ForCodePage(65001).GetPreamble());
    }

    [Fact]
    public void StartInfo_UsesTheConsoleEncodingForAllThreeStreams()
    {
        var startInfo = CdbProcessManager.CreateStartInfo("cdb", "dump.dmp", "cache*x");

        Assert.Same(CdbConsoleEncoding.Current, startInfo.StandardOutputEncoding);
        Assert.Same(CdbConsoleEncoding.Current, startInfo.StandardErrorEncoding);
        Assert.Same(CdbConsoleEncoding.Current, startInfo.StandardInputEncoding);
        if (!OperatingSystem.IsWindows())
            Assert.IsType<UTF8Encoding>(CdbConsoleEncoding.Current);
    }
}
