using DumpAnalysisService.Infrastructure.Debugger;

namespace DumpAnalysisService.Tests.Infrastructure;

public class CdbProcessManagerTests
{
    [Fact]
    public void CreateStartInfo_PassesEachValueAsSeparateArgument()
    {
        // A value with quotes and spaces must stay one argument.
        const string symbolPath = "cache*C:\\symbols;C:\\a \"b\" -c x";

        var startInfo = CdbProcessManager.CreateStartInfo(@"C:\cdb.exe", @"C:\dumps\my dump.dmp", symbolPath);

        Assert.Equal(["-noshell", "-z", @"C:\dumps\my dump.dmp", "-y", symbolPath], startInfo.ArgumentList);
        Assert.Equal(string.Empty, startInfo.Arguments);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.RedirectStandardError);
    }
}
