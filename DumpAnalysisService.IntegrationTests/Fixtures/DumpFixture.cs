using System.Diagnostics;

namespace DumpAnalysisService.IntegrationTests.Fixtures;

public sealed class DumpFixture : IAsyncLifetime
{
    public string DumpPath { get; private set; } = string.Empty;
    private string _workDir = string.Empty;

    public async Task InitializeAsync()
    {
        _workDir = Path.Combine(Path.GetTempPath(),
            "mcp-windbg-it",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);
        DumpPath = Path.Combine(_workDir, "crasher.dmp");

        var crasherExe = ResolveCrasherExePath();
        if (!File.Exists(crasherExe))
            throw new FileNotFoundException(
                $"TestCrasher.exe not found at: {crasherExe}. " +
                "Check the IntegrationTests csproj CopyTestCrasher target.");

        var psi = new ProcessStartInfo
        {
            FileName = crasherExe,
            ArgumentList = { DumpPath, "default" },
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start TestCrasher.");

        var stderr = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();

        if (proc.ExitCode != 0)
            throw new InvalidOperationException(
                $"TestCrasher exited {proc.ExitCode}. Stderr: {stderr}");

        var info = new FileInfo(DumpPath);
        if (!info.Exists || info.Length < 5 * 1024 * 1024)
            throw new InvalidOperationException(
                $"Dump file too small or missing: {info.FullName} " +
                $"(size={(info.Exists ? info.Length : -1)} bytes)");
    }

    public Task DisposeAsync()
    {
        try
        {
            if (Directory.Exists(_workDir))
                Directory.Delete(_workDir, recursive: true);
        }
        catch
        {
            // best-effort cleanup; CI temp dir gets wiped between jobs anyway.
        }
        return Task.CompletedTask;
    }

    private static string ResolveCrasherExePath()
    {
        var asmDir = Path.GetDirectoryName(
            typeof(DumpFixture).Assembly.Location)!;
        return Path.Combine(asmDir, "TestCrasher", "DumpAnalysisService.TestCrasher.exe");
    }
}
