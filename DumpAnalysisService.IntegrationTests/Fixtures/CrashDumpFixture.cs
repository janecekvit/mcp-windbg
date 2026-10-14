using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using DumpAnalysisService.Infrastructure.Detection;
using DumpAnalysisService.Infrastructure.IO;
using Microsoft.Extensions.Logging.Abstractions;

namespace DumpAnalysisService.IntegrationTests.Fixtures;

/// <summary>
/// Produces crash dumps from the NativeAOT CrashScenarios program on demand.
/// Each scenario runs under cdb, which writes a full dump at the exception the way an
/// attached debugger or a post-mortem dumper would, so the dump carries the exception
/// record and the faulting thread's context. Dumps are cached per (scenario, run).
/// </summary>
public sealed class CrashDumpFixture : IAsyncLifetime
{
    /// <summary>
    /// Debugger events that end a scenario: access violation, integer divide by zero,
    /// stack overflow, break instruction (deadlock scenario), fail-fast, the scenario's
    /// application-defined code, and the .NET exception code (NativeAOT raises a thrown
    /// managed exception as SEH 0xe0434352).
    /// </summary>
    private static readonly string[] _dumpOnEvents =
        ["av", "dz", "sov", "bpe", "0xc0000409", "0xe0001234", "0xe0434352"];

    private static readonly TimeSpan _scenarioTimeout = TimeSpan.FromMinutes(3);

    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _dumps = new();
    private string _workDir = string.Empty;

    public static string ScenarioDirectory =>
        Path.Combine(Path.GetDirectoryName(typeof(CrashDumpFixture).Assembly.Location)!, "CrashScenarios");

    public static string ScenarioExePath => Path.Combine(ScenarioDirectory, "CrashScenarios.exe");

    public static bool IsAvailable => CdbAvailability.IsAvailable && File.Exists(ScenarioExePath);

    public const string SkipReason =
        "Needs cdb.exe and the NativeAOT CrashScenarios.exe (published on Windows by the PublishCrashScenarios target).";

    public Task InitializeAsync()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "mcp-windbg-crash", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);

        // The path is embedded unquoted in cdb commands below.
        if (_workDir.IndexOfAny([' ', ';', '"']) >= 0)
            throw new InvalidOperationException($"Crash dump work directory must not contain spaces, ';' or quotes: {_workDir}");

        return Task.CompletedTask;
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

    /// <summary>
    /// Returns the dump of <paramref name="scenario"/>; <paramref name="run"/> > 1 runs the
    /// scenario again in a new process and returns a separate dump.
    /// </summary>
    public Task<string> GetDumpAsync(string scenario, int run = 1) =>
        _dumps.GetOrAdd($"{scenario}-{run}", key => new Lazy<Task<string>>(() => _CreateDumpAsync(scenario, key))).Value;

    private async Task<string> _CreateDumpAsync(string scenario, string name)
    {
        var cdbPath = _FindCdb();
        var dumpPath = Path.Combine(_workDir, name + ".dmp");
        var scriptPath = Path.Combine(_workDir, name + ".cdb.txt");

        // At the initial breakpoint: dump and quit on any of the scenario-ending events, then run.
        // Inside a quoted sx command string cdb treats '\' as an escape character
        // ("\r" in C:\Users\runneradmin would become a carriage return), so double it.
        var dumpCommand = $".dump /ma /o {dumpPath.Replace(@"\", @"\\")}; q";
        var script = new StringBuilder();
        foreach (var debuggerEvent in _dumpOnEvents)
            script.AppendLine($"sxe -c \"{dumpCommand}\" -c2 \"{dumpCommand}\" {debuggerEvent}");
        script.AppendLine("g");
        await File.WriteAllTextAsync(scriptPath, script.ToString());

        var psi = new ProcessStartInfo
        {
            FileName = cdbPath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-cf");
        psi.ArgumentList.Add(scriptPath);
        psi.ArgumentList.Add(ScenarioExePath);
        psi.ArgumentList.Add(scenario);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start cdb.exe");
        process.StandardInput.Close();

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(_scenarioTimeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"Scenario '{scenario}' did not produce a dump within {_scenarioTimeout}.");
        }

        var info = new FileInfo(dumpPath);
        if (!info.Exists || info.Length == 0)
        {
            var output = await stdout + await stderr;
            throw new InvalidOperationException(
                $"Scenario '{scenario}' produced no dump. cdb exit code {process.ExitCode}. Output tail:\n" +
                output[Math.Max(0, output.Length - 4000)..]);
        }

        return dumpPath;
    }

    private static string _FindCdb()
    {
        var detection = new PathDetectionService(
            NullLogger<PathDetectionService>.Instance,
            new PathExpansionService(NullLogger<PathExpansionService>.Instance));
        return detection.GetBestDebuggerPath()
            ?? throw new InvalidOperationException("cdb.exe not found");
    }
}
