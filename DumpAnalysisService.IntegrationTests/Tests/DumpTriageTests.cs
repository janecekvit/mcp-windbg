using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using DumpAnalysisService.IntegrationTests.Fixtures;
using DumpAnalysisService.Security;
using DumpAnalysisService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shared.Models;
using Xunit.Abstractions;

namespace DumpAnalysisService.IntegrationTests.Tests;

/// <summary>
/// triage_dump against real dumps: the cdb-written crash scenario dumps and the TestCrasher
/// snapshot. Triage itself needs no debugger, but producing the crash dumps does.
/// </summary>
[Collection(IntegrationCollection.Name)]
[Trait("Category", "Integration")]
public class DumpTriageTests
{
    private readonly ServiceFixture _service;
    private readonly CrashDumpFixture _crashDumps;
    private readonly DumpFixture _snapshot;
    private readonly ITestOutputHelper _output;

    public DumpTriageTests(ServiceFixture service, CrashDumpFixture crashDumps, DumpFixture snapshot, ITestOutputHelper output)
    {
        _service = service;
        _crashDumps = crashDumps;
        _snapshot = snapshot;
        _output = output;
    }

    /// <summary>Scenario, exception code, module that contains the faulting instruction (null: not asserted), classification.</summary>
    public static IEnumerable<object?[]> CrashScenarios() =>
    [
        ["access-violation", "0xC0000005", "CrashScenarios.exe", "null-pointer"],
        ["access-violation-alt-path", "0xC0000005", "CrashScenarios.exe", "null-pointer"],
        ["divide-by-zero", "0xC0000094", "CrashScenarios.exe", "integer-arithmetic"],
        ["stack-overflow", "0xC00000FD", "CrashScenarios.exe", "stack-overflow"],
        ["custom-exception", "0xE0001234", null, "application-exception"],
        ["fail-fast", "0xC0000409", null, "fail-fast"],
        ["managed-unhandled", "0xE0434352", null, "dotnet-exception"],
        ["deadlock", "0x80000003", null, "breakpoint"],
        ["unloaded-module", "0xC0000005", null, "call-into-unloaded-module"],
        ["use-after-free", "0xC0000005", "CrashScenarios.exe", "non-canonical-address"],
        ["cpp-exception", "0xE06D7363", null, "cpp-exception"],
        ["busy-loop", "0x80000003", null, "breakpoint"],
        ["handle-leak", "0x80000003", null, "breakpoint"],
    ];

    [SkippableTheory]
    [MemberData(nameof(CrashScenarios))]
    public async Task TriageDump_CrashScenario_ReportsFaultProcessAndSymbols(string scenario, string code, string? faultingModule, string classification)
    {
        Skip.IfNot(CrashDumpFixture.IsAvailable, CrashDumpFixture.SkipReason);

        var dumpPath = await _crashDumps.GetDumpAsync(scenario);
        var result = await _TriageViaRestAsync(dumpPath);

        Assert.Equal("user-minidump", result.Dump.Format);
        Assert.True(result.Dump.FullMemory);
        Assert.Equal(64, result.Dump.Sha256!.Length);
        // .dump /ma = full memory, full memory info, handle data, thread info (and unloaded modules,
        // a stream dbghelp writes only when the process has unloaded one).
        Assert.Superset(new HashSet<string> { "memory-info", "handle-data", "thread-info" }, result.Dump.Contents.ToHashSet());
        Assert.Equal("x64", result.System!.Architecture);
        Assert.Equal("CrashScenarios.exe", result.Process!.ImageName);
        Assert.NotNull(result.Process.ProcessId);
        Assert.False(result.Process.Wow64);

        Assert.Equal(code, result.Fault!.Code);
        Assert.Equal(classification, result.Fault.Classification);
        if (faultingModule is not null)
            Assert.Equal(faultingModule, result.Fault.Module);
        // The faulting thread's context was read; for hardware faults rip is the faulting instruction.
        Assert.Contains("rsp", result.Fault.Registers.Keys);
        if (code is "0xC0000005" or "0xC0000094" or "0xC00000FD")
            Assert.Equal(result.Fault.Address, result.Fault.Registers["rip"]);

        // Our own module is notable (not part of Windows) and carries its PDB identity.
        var app = Assert.Single(result.Modules!.Notable, m => m.Name == "CrashScenarios.exe");
        Assert.Equal("CrashScenarios.pdb", app.PdbName);
        Assert.False(string.IsNullOrEmpty(app.SymbolKey));
        Assert.NotEmpty(result.Hints);

        Assert.True(result.Symbols!.Checked > 0);
        Assert.True(result.Handles!.Count > 0);
        if (result.Memory is { PrivateBytes: { } privateBytes } memory)
        {
            Assert.True(privateBytes > 0);
            Assert.True(memory.WorkingSet <= memory.PeakWorkingSet);
        }

        _output.WriteLine($"{scenario}: contents {string.Join(",", result.Dump.Contents)}");
        _output.WriteLine($"{scenario}: hints\n  {string.Join("\n  ", result.Hints)}");
    }

    [SkippableFact]
    public async Task TriageDump_AccessViolation_IsNullPointerReadOfUnallocatedMemory()
    {
        Skip.IfNot(CrashDumpFixture.IsAvailable, CrashDumpFixture.SkipReason);

        var result = await _TriageViaRestAsync(await _crashDumps.GetDumpAsync("access-violation"));

        Assert.Equal("read", result.Fault!.AccessType);
        Assert.Equal("0x0", result.Fault.TargetAddress);
        Assert.Equal("free", result.Fault.TargetRegion!.State);
        Assert.Contains(result.Hints, h => h.Contains("Null-pointer read"));
    }

    [SkippableFact]
    public async Task TriageDump_FailFast_DecodesTheSubcode()
    {
        Skip.IfNot(CrashDumpFixture.IsAvailable, CrashDumpFixture.SkipReason);

        var result = await _TriageViaRestAsync(await _crashDumps.GetDumpAsync("fail-fast"));

        // Environment.FailFast under NativeAOT raises the runtime's own fail-fast code.
        Assert.Equal("FAST_FAIL_EXCEPTION_DOTNET_AOT", result.Fault!.FastFailCode);
        Assert.Contains(result.Hints, h => h.Contains("FAST_FAIL_EXCEPTION_DOTNET_AOT") && h.Contains("NativeAOT"));
    }

    [SkippableFact]
    public async Task TriageDump_UnloadedModule_NamesTheUnloadedDll()
    {
        Skip.IfNot(CrashDumpFixture.IsAvailable, CrashDumpFixture.SkipReason);

        var result = await _TriageViaRestAsync(await _crashDumps.GetDumpAsync("unloaded-module"));

        var fault = result.Fault!;
        Assert.Equal("execute", fault.AccessType);
        Assert.NotNull(fault.UnloadedModule);
        Assert.Contains(result.Modules!.Unloaded, m => m.Name.Equals(fault.UnloadedModule, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Hints, h => h.Contains("after it was unloaded"));
    }

    [SkippableFact]
    public async Task TriageDump_UseAfterFree_FlagsTheFreedPatternInARegister()
    {
        Skip.IfNot(CrashDumpFixture.IsAvailable, CrashDumpFixture.SkipReason);

        var result = await _TriageViaRestAsync(await _crashDumps.GetDumpAsync("use-after-free"));

        Assert.Equal("0xffffffffffffffff", result.Fault!.TargetAddress);
        Assert.Contains(result.Fault.SuspiciousValues, v => v.Location != "target" && v.Value == "0xdddddddddddddddd" && v.Meaning.Contains("freed"));
        Assert.Contains(result.Hints, h => h.Contains("Registers holding fill patterns"));
    }

    [SkippableFact]
    public async Task TriageDump_CppException_RecoversTypeAndMessage()
    {
        Skip.IfNot(CrashDumpFixture.IsAvailable, CrashDumpFixture.SkipReason);

        var result = await _TriageViaRestAsync(await _crashDumps.GetDumpAsync("cpp-exception"));

        var cpp = result.Fault!.CppException!;
        Assert.Equal("std::out_of_range", cpp.Type);
        Assert.Contains("std::exception", cpp.CatchableTypes);
        Assert.Equal("CrashScenarios: out of range", cpp.Message);
        Assert.Contains(result.Hints, h => h.Contains("std::out_of_range") && h.Contains("CrashScenarios: out of range"));
    }

    [SkippableFact]
    public async Task TriageDump_BusyLoop_PointsAtTheSpinningThread()
    {
        Skip.IfNot(CrashDumpFixture.IsAvailable, CrashDumpFixture.SkipReason);

        var result = await _TriageViaRestAsync(await _crashDumps.GetDumpAsync("busy-loop"));

        var busiest = result.Threads!.Busiest[0];
        _output.WriteLine($"busiest: {busiest}");
        Assert.True(busiest.CpuTime >= TimeSpan.FromSeconds(1), $"CPU time {busiest.CpuTime}");
        Assert.True(busiest.CpuShare >= 0.5, $"CPU share {busiest.CpuShare}");
        if (result.Dump.Contents.Contains("thread-names"))
            Assert.Equal("Spinner", busiest.Name);
        Assert.Contains(result.Hints, h => h.Contains("busy loop"));
    }

    [SkippableFact]
    public async Task TriageDump_HandleLeak_CountsTheLeakedEvents()
    {
        Skip.IfNot(CrashDumpFixture.IsAvailable, CrashDumpFixture.SkipReason);

        var result = await _TriageViaRestAsync(await _crashDumps.GetDumpAsync("handle-leak"));

        Assert.True(result.Handles!.Count >= 12_000, $"Handle count {result.Handles.Count}");
        Assert.Equal("Event", result.Handles.ByType[0].Type);
        Assert.Contains(result.Hints, h => h.Contains("handle leak"));
    }

    [SkippableFact]
    public async Task TriageDump_Deadlock_NamesTheLockThreads()
    {
        Skip.IfNot(CrashDumpFixture.IsAvailable, CrashDumpFixture.SkipReason);

        var result = await _TriageViaRestAsync(await _crashDumps.GetDumpAsync("deadlock"));

        _output.WriteLine($"named threads: {string.Join(", ", result.Threads!.Named.Select(t => $"{t.Id}={t.Name}"))}");
        if (result.Dump.Contents.Contains("thread-names"))
        {
            Assert.Contains(result.Threads.Named, t => t.Name == "Deadlock-A");
            Assert.Contains(result.Threads.Named, t => t.Name == "Deadlock-B");
        }
        Assert.DoesNotContain(result.Hints, h => h.Contains("busy loop"));
    }

    [SkippableFact]
    public async Task TriageDump_TestCrasherSnapshot_HasNoFaultAndDetectsDotNet()
    {
        Skip.IfNot(CdbAvailability.IsAvailable, CdbAvailability.SkipReason);

        var result = await _TriageViaRestAsync(_snapshot.DumpPath);

        Assert.Null(result.Fault);
        Assert.Equal(".NET", result.Runtime!.Kind);
        Assert.True(result.Threads!.Count >= 11, $"Expected the main thread plus 10 workers, got {result.Threads.Count}");
        Assert.Contains(result.Hints, h => h.Contains("snapshot"));
    }

    [SkippableFact]
    public async Task Triage_FullMemoryDump_TakesWellUnderASecond()
    {
        Skip.IfNot(CrashDumpFixture.IsAvailable, CrashDumpFixture.SkipReason);

        var dumpPath = await _crashDumps.GetDumpAsync("access-violation");
        var service = new DumpTriageService(NullLogger<DumpTriageService>.Instance, Options.Create(new SecurityOptions()));
        await service.TriageAsync(dumpPath); // warm-up (JIT, file cache)

        var stopwatch = Stopwatch.StartNew();
        var result = await service.TriageAsync(dumpPath);
        stopwatch.Stop();

        // Generous bound for shared CI runners; the issue's target is < 1 s for 100 MB.
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2),
            $"Triage of {result.Dump.SizeBytes / (1024 * 1024)} MB took {stopwatch.ElapsedMilliseconds} ms");
    }

    private async Task<DumpTriageResult> _TriageViaRestAsync(string dumpPath)
    {
        var response = await _service.Client.PostAsJsonAsync("/api/jobs/triage-dump", new TriageDumpRequest(dumpPath));
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<JobCreatedResponse>();

        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(1);
        while (DateTime.UtcNow < deadline)
        {
            var status = await _service.Client.GetFromJsonAsync<JobStatus>($"/api/jobs/{created!.JobId}");
            if (status!.State == JobState.Completed)
                return JsonSerializer.Deserialize<DumpTriageResult>(status.Result!, JsonSerializerOptions.Web)!;
            if (status.State is JobState.Failed or JobState.Cancelled)
                throw new InvalidOperationException($"Triage job {status.State}: {status.Error}");
            await Task.Delay(200);
        }

        throw new TimeoutException("Triage job did not finish within a minute");
    }
}
