using System.Collections;
using System.Diagnostics;
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

    [Fact]
    public void CreateStartInfo_PassesOnlyTheMinimalEnvironment()
    {
        var source = new Hashtable
        {
            ["SystemRoot"] = @"C:\Windows",
            ["PATH"] = @"C:\Windows\system32",
            ["TEMP"] = @"C:\Temp",
            ["PROCESSOR_ARCHITECTURE"] = "AMD64",
            ["Security__ApiKey"] = "placeholder",
            ["_NT_SYMBOL_PATH"] = "placeholder",
            ["SOME_SERVICE_SETTING"] = "placeholder",
            ["EXTRA_VARIABLE"] = "kept",
        };

        var startInfo = CdbProcessManager.CreateStartInfo(@"C:\cdb.exe", "dump.dmp", "cache*x", ["EXTRA_VARIABLE"], source);

        Assert.Equal(
            ["EXTRA_VARIABLE", "PATH", "PROCESSOR_ARCHITECTURE", "SystemRoot", "TEMP"],
            startInfo.Environment.Keys.Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(@"C:\Windows", startInfo.Environment["SystemRoot"]);
        Assert.Equal("kept", startInfo.Environment["EXTRA_VARIABLE"]);
    }

    [Fact]
    public void CreateStartInfo_WithoutSourceEnvironment_DropsServiceVariables()
    {
        Environment.SetEnvironmentVariable("DUMPANALYSIS_TEST_SECRET", "placeholder");
        try
        {
            var startInfo = CdbProcessManager.CreateStartInfo("cdb", "dump.dmp", "cache*x");

            Assert.False(startInfo.Environment.ContainsKey("DUMPANALYSIS_TEST_SECRET"));
            Assert.All(startInfo.Environment.Keys, name => Assert.True(
                CdbProcessManager.InheritedEnvironmentVariables.Contains(name, StringComparer.OrdinalIgnoreCase)
                || name.StartsWith("PROCESSOR_", StringComparison.OrdinalIgnoreCase), name));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DUMPANALYSIS_TEST_SECRET", null);
        }
    }

    [Fact]
    public void JobObject_WithoutChildProcesses_StopsTheProcessFromStartingOne()
    {
        if (!OperatingSystem.IsWindows())
            return;

        // Control: the same command line starts a child process outside the job.
        Assert.Contains("85547", _RunShellWithChild(job: null));

        using var job = new CdbJobObject(allowChildProcesses: false);
        Assert.DoesNotContain("85547", _RunShellWithChild(job));
    }

    [Fact]
    public void JobObject_Dispose_EndsTheProcess()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var process = Process.Start(new ProcessStartInfo("cmd.exe") { RedirectStandardInput = true, UseShellExecute = false, CreateNoWindow = true })!;
        var job = new CdbJobObject(allowChildProcesses: false);
        job.Assign(process);

        job.Dispose();

        Assert.True(process.WaitForExit(10_000));
    }

    /// <summary>Starts cmd.exe, optionally puts it in the job, then asks it to run a child cmd.exe that prints 7777*11.</summary>
    private static string _RunShellWithChild(CdbJobObject? job)
    {
        if (!OperatingSystem.IsWindows())
            return string.Empty;

        var startInfo = new ProcessStartInfo("cmd.exe", "/Q")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(startInfo)!;
        job?.Assign(process);

        process.StandardInput.WriteLine("cmd.exe /c set /a 7777*11");
        process.StandardInput.WriteLine("exit");
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(10_000);
        return output;
    }
}
