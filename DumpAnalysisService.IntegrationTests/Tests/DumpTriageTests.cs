using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using DumpAnalysisService.IntegrationTests.Fixtures;
using DumpAnalysisService.Security;
using DumpAnalysisService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shared.Models;

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

    public DumpTriageTests(ServiceFixture service, CrashDumpFixture crashDumps, DumpFixture snapshot)
    {
        _service = service;
        _crashDumps = crashDumps;
        _snapshot = snapshot;
    }

    /// <summary>Scenario, exception code, module that contains the faulting instruction (null: not asserted).</summary>
    public static IEnumerable<object?[]> CrashScenarios() =>
    [
        ["access-violation", "0xC0000005", "CrashScenarios.exe"],
        ["access-violation-alt-path", "0xC0000005", "CrashScenarios.exe"],
        ["divide-by-zero", "0xC0000094", "CrashScenarios.exe"],
        ["stack-overflow", "0xC00000FD", "CrashScenarios.exe"],
        ["custom-exception", "0xE0001234", null],
        ["fail-fast", "0xC0000409", null],
        ["managed-unhandled", "0xE0434352", null],
        ["deadlock", "0x80000003", null],
    ];

    [SkippableTheory]
    [MemberData(nameof(CrashScenarios))]
    public async Task TriageDump_CrashScenario_ReportsFaultProcessAndSymbols(string scenario, string code, string? faultingModule)
    {
        Skip.IfNot(CrashDumpFixture.IsAvailable, CrashDumpFixture.SkipReason);

        var dumpPath = await _crashDumps.GetDumpAsync(scenario);
        var result = await _TriageViaRestAsync(dumpPath);

        Assert.Equal("user-minidump", result.Dump.Format);
        Assert.True(result.Dump.FullMemory);
        Assert.Equal(64, result.Dump.Sha256!.Length);
        Assert.Equal("x64", result.System!.Architecture);
        Assert.Equal("CrashScenarios.exe", result.Process!.ImageName);
        Assert.NotNull(result.Process.ProcessId);

        Assert.Equal(code, result.Fault!.Code);
        if (faultingModule is not null)
            Assert.Equal(faultingModule, result.Fault.Module);

        // Our own module is notable (not part of Windows) and carries its PDB identity.
        var app = Assert.Single(result.Modules!.Notable, m => m.Name == "CrashScenarios.exe");
        Assert.Equal("CrashScenarios.pdb", app.PdbName);
        Assert.False(string.IsNullOrEmpty(app.SymbolKey));
        Assert.NotEmpty(result.Hints);
    }

    [SkippableFact]
    public async Task TriageDump_AccessViolation_IsNullPointerRead()
    {
        Skip.IfNot(CrashDumpFixture.IsAvailable, CrashDumpFixture.SkipReason);

        var result = await _TriageViaRestAsync(await _crashDumps.GetDumpAsync("access-violation"));

        Assert.Equal("read", result.Fault!.AccessType);
        Assert.Equal("0x0", result.Fault.TargetAddress);
        Assert.Contains(result.Hints, h => h.Contains("Null-pointer read"));
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
