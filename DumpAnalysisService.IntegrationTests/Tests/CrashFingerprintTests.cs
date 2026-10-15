using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DumpAnalysisService.IntegrationTests.Fixtures;
using Shared.Configuration;
using Shared.Models;
using Xunit.Abstractions;

namespace DumpAnalysisService.IntegrationTests.Tests;

/// <summary>
/// fingerprint_crash over the CrashScenarios matrix: real cdb stacks with our own PDB.
/// </summary>
[Collection(IntegrationCollection.Name)]
[Trait("Category", "Integration")]
public class CrashFingerprintTests
{
    /// <summary>Crash scenarios (hang dumps are out of scope) and a function expected among the fingerprint frames.</summary>
    private static readonly (string Scenario, string Frame)[] _scenarios =
    [
        ("access-violation", "ReadThroughPointer"),
        ("access-violation-alt-path", "LoadFromCache"),
        ("divide-by-zero", "ComputeAverage"),
        ("stack-overflow", "RecurseWithoutLimit"),
        ("custom-exception", "ReportInvalidState"),
        ("fail-fast", "AbortOnCorruption"),
        ("managed-unhandled", "ParseConfiguration"),
        ("unloaded-module", "CallIntoUnloadedPlugin"),
        ("use-after-free", "ReadReleasedRecord"),
        ("cpp-exception", "ThrowFromNativeLibrary"),
    ];

    private readonly ServiceFixture _service;
    private readonly CrashDumpFixture _crashDumps;
    private readonly ITestOutputHelper _output;

    public CrashFingerprintTests(ServiceFixture service, CrashDumpFixture crashDumps, ITestOutputHelper output)
    {
        _service = service;
        _crashDumps = crashDumps;
        _output = output;
    }

    [SkippableFact]
    public async Task EveryCrashScenario_HasAGroupableFingerprint_AndAllDiffer()
    {
        Skip.IfNot(CrashDumpFixture.IsAvailable, CrashDumpFixture.SkipReason);

        var problems = new StringBuilder();
        var fingerprints = new Dictionary<string, string>();
        foreach (var (scenario, frame) in _scenarios)
        {
            var result = await _FingerprintAsync(await _crashDumps.GetDumpAsync(scenario));
            _output.WriteLine($"{scenario}: {result.Fingerprint} {result.Quality} | {string.Join(" > ", result.Frames)}");

            if (result.Fingerprint is null || result.Quality is not ("exact" or "partial") || !result.Recorded)
                problems.AppendLine($"{scenario}: expected a recorded exact/partial fingerprint, got {result.Fingerprint} {result.Quality} (note: {result.Note})");
            if (!result.Frames.Any(f => f.Contains(frame, StringComparison.Ordinal)))
                problems.AppendLine($"{scenario}: {frame} is not among the frames {string.Join(" > ", result.Frames)}");
            if (result.Fingerprint is { } fingerprint)
                fingerprints[scenario] = fingerprint;
        }

        foreach (var pair in fingerprints.GroupBy(f => f.Value).Where(g => g.Count() > 1))
            problems.AppendLine($"Same fingerprint {pair.Key} for {string.Join(", ", pair.Select(p => p.Key))}");

        Assert.True(problems.Length == 0, problems.ToString());
    }

    [SkippableFact]
    public async Task SameScenarioInTwoProcesses_HasTheSameFingerprint_AndCountsTwice()
    {
        Skip.IfNot(CrashDumpFixture.IsAvailable, CrashDumpFixture.SkipReason);

        var first = await _FingerprintAsync(await _crashDumps.GetDumpAsync("access-violation", run: 1));
        var second = await _FingerprintAsync(await _crashDumps.GetDumpAsync("access-violation", run: 2));

        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.True(second.IsNewDump);
        Assert.False(second.IsNew);
        Assert.True(second.Occurrences >= 2);

        var groups = await _service.Client.GetFromJsonAsync<CrashGroupList>("/api/crash-groups", JsonSerializerOptions.Web);
        var group = Assert.Single(groups!.Groups, g => g.Fingerprint == first.Fingerprint);
        Assert.Equal(second.Occurrences, group.Occurrences);
    }

    [SkippableFact]
    public async Task SameDumpTwice_IsCountedOnce()
    {
        Skip.IfNot(CrashDumpFixture.IsAvailable, CrashDumpFixture.SkipReason);

        var dumpPath = await _crashDumps.GetDumpAsync("divide-by-zero");
        var sessionId = await _LoadDumpAsync(dumpPath);
        try
        {
            var first = await _RunFingerprintJobAsync(sessionId);
            var second = await _RunFingerprintJobAsync(sessionId);

            Assert.Equal(first.Fingerprint, second.Fingerprint);
            Assert.False(second.IsNewDump);
            Assert.Equal(first.Occurrences, second.Occurrences);
        }
        finally
        {
            await _CloseSessionAsync(sessionId);
        }
    }

    [SkippableFact]
    public async Task HangDump_IsNotGrouped()
    {
        Skip.IfNot(CrashDumpFixture.IsAvailable, CrashDumpFixture.SkipReason);

        var result = await _FingerprintAsync(await _crashDumps.GetDumpAsync("deadlock"));

        Assert.Equal("weak", result.Quality);
        Assert.False(result.Recorded);
    }

    private async Task<CrashFingerprintResult> _FingerprintAsync(string dumpPath)
    {
        var sessionId = await _LoadDumpAsync(dumpPath);
        try
        {
            return await _RunFingerprintJobAsync(sessionId);
        }
        finally
        {
            await _CloseSessionAsync(sessionId);
        }
    }

    private async Task<CrashFingerprintResult> _RunFingerprintJobAsync(string sessionId)
    {
        var json = await _RunJobAsync("/api/jobs/fingerprint-crash", new FingerprintCrashRequest(sessionId), TimeSpan.FromMinutes(10));
        return JsonSerializer.Deserialize<CrashFingerprintResult>(json, JsonSerializerOptions.Web)!;
    }

    private async Task<string> _LoadDumpAsync(string dumpPath)
    {
        // Our PDB sits next to CrashScenarios.exe
        var symbols = new SymbolsConfiguration(SymbolPathExtra: CrashDumpFixture.ScenarioDirectory);
        var result = await _RunJobAsync("/api/jobs/load-dump", new LoadDumpRequest(dumpPath, symbols), TimeSpan.FromMinutes(20));
        Assert.False(string.IsNullOrWhiteSpace(result));
        return result;
    }

    private async Task _CloseSessionAsync(string sessionId)
    {
        try
        {
            await _RunJobAsync("/api/jobs/close-session", new CloseSessionRequest(sessionId), TimeSpan.FromMinutes(1));
        }
        catch
        {
            // best-effort cleanup
        }
    }

    private async Task<string> _RunJobAsync<TRequest>(string url, TRequest body, TimeSpan timeout)
    {
        var response = await _service.Client.PostAsJsonAsync(url, body);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<JobCreatedResponse>();
        Assert.NotNull(created);

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var status = await _service.Client.GetFromJsonAsync<JobStatus>($"/api/jobs/{created!.JobId}");
            Assert.NotNull(status);
            switch (status!.State)
            {
                case JobState.Completed:
                    return status.Result ?? string.Empty;
                case JobState.Failed or JobState.Cancelled:
                    throw new InvalidOperationException($"Job {created.JobId} ({url}) ended {status.State}: {status.Error}");
            }
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        throw new TimeoutException($"Job {created!.JobId} ({url}) did not finish within {timeout}.");
    }
}
