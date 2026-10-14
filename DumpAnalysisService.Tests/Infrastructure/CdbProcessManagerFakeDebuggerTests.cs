using DumpAnalysisService.Infrastructure.Debugger;
using Microsoft.Extensions.Logging.Abstractions;

namespace DumpAnalysisService.Tests.Infrastructure;

/// <summary>
/// Drives <see cref="CdbProcessManager"/> against a tiny shell script that mimics CDB's
/// stdin/stdout protocol (prompt, .echo, q). Runs only where /bin/sh exists; on Windows
/// the integration tests cover the real cdb.exe.
/// </summary>
public sealed class CdbProcessManagerFakeDebuggerTests : IDisposable
{
    private const string FakeCdbScript = """
        #!/bin/sh
        printf '0:000> '
        while IFS= read -r line; do
          case "$line" in
            q) exit 0 ;;
            ".echo "*) printf '%s\n' "${line#.echo }" ;;
            slow) sleep 2; printf 'slow done\n' ;;
            *) printf 'out: %s\n' "$line" ;;
          esac
          printf '0:000> '
        done
        """;

    private readonly string? _scriptPath;
    private readonly CdbProcessManager _manager = new("fake", NullLogger<CdbProcessManager>.Instance);

    public CdbProcessManagerFakeDebuggerTests()
    {
        if (OperatingSystem.IsWindows() || !File.Exists("/bin/sh"))
            return;

        _scriptPath = Path.Combine(Path.GetTempPath(), $"fake-cdb-{Guid.NewGuid():N}.sh");
        File.WriteAllText(_scriptPath, FakeCdbScript.Replace("\r\n", "\n") + "\n");
        File.SetUnixFileMode(_scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    public void Dispose()
    {
        _manager.Dispose();
        if (_scriptPath != null)
            File.Delete(_scriptPath);
    }

    private async Task<bool> _StartAsync()
    {
        if (_scriptPath == null)
            return false;

        Assert.True(await _manager.StartProcessAsync(_scriptPath, "dump.dmp", "cache*/tmp"));
        return true;
    }

    [Fact]
    public async Task ExecuteCommandAsync_ReturnsCommandOutputWithoutPrompts()
    {
        if (!await _StartAsync())
            return;

        var result = await _manager.ExecuteCommandAsync("kb", 5000);

        Assert.Equal("out: kb", result);
    }

    [Fact]
    public async Task ExecuteCommandAsync_ConsecutiveCommands_DoNotMixOutput()
    {
        if (!await _StartAsync())
            return;

        Assert.Equal("out: first", await _manager.ExecuteCommandAsync("first", 5000));
        Assert.Equal("out: second", await _manager.ExecuteCommandAsync("second", 5000));
    }

    [Fact]
    public async Task ExecuteCommandAsync_AfterTimeout_NextCommandGetsOnlyItsOwnOutput()
    {
        if (!await _StartAsync())
            return;

        await Assert.ThrowsAsync<TimeoutException>(() => _manager.ExecuteCommandAsync("slow", 200));

        var result = await _manager.ExecuteCommandAsync("lm", 5000);

        Assert.Equal("out: lm", result);
    }

    [Fact]
    public async Task ShutdownGracefullyAsync_StopsProcess()
    {
        if (!await _StartAsync())
            return;

        await _manager.ShutdownGracefullyAsync(5000);

        Assert.False(_manager.IsActive);
    }
}
