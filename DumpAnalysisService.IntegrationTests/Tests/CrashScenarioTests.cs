using System.Net.Http.Json;
using DumpAnalysisService.IntegrationTests.Fixtures;
using Shared.Configuration;
using Shared.Models;

namespace DumpAnalysisService.IntegrationTests.Tests;

/// <summary>
/// Loads each CrashScenarios dump through the REST API and checks that cdb sees the expected
/// exception and our own function names on the faulting stack.
/// </summary>
[Collection(IntegrationCollection.Name)]
[Trait("Category", "Integration")]
public class CrashScenarioTests
{
    private readonly ServiceFixture _service;
    private readonly CrashDumpFixture _crashDumps;

    public CrashScenarioTests(ServiceFixture service, CrashDumpFixture crashDumps)
    {
        _service = service;
        _crashDumps = crashDumps;
    }

    /// <summary>Scenario, exception code as printed by .exr, functions expected on the faulting stack.</summary>
    public static IEnumerable<object[]> CrashScenarios() =>
    [
        ["access-violation", "c0000005", new[] { "ReadThroughPointer", "ValidateRecord", "PrepareRecord" }],
        ["access-violation-alt-path", "c0000005", new[] { "ReadThroughPointer", "LoadFromCache" }],
        ["divide-by-zero", "c0000094", new[] { "ComputeAverage" }],
        ["stack-overflow", "c00000fd", new[] { "RecurseWithoutLimit" }],
        ["custom-exception", "e0001234", new[] { "ReportInvalidState" }],
        ["fail-fast", "c0000409", new[] { "AbortOnCorruption" }],
        // NativeAOT raises the thrown exception as SEH e0434352; the dump is taken at the throw.
        ["managed-unhandled", "e0434352", new[] { "ParseConfiguration" }],
    ];

    [SkippableTheory]
    [MemberData(nameof(CrashScenarios))]
    public async Task CrashScenario_ShowsExceptionAndFaultingFrames(string scenario, string exceptionCode, string[] expectedFrames)
    {
        Skip.IfNot(CrashDumpFixture.IsAvailable, CrashDumpFixture.SkipReason);

        var dumpPath = await _crashDumps.GetDumpAsync(scenario);
        var sessionId = await _LoadDumpAsync(dumpPath);
        try
        {
            var exceptionRecord = await _ExecuteAsync(sessionId, ".exr -1");
            Assert.Contains($"ExceptionCode: {exceptionCode}", exceptionRecord, StringComparison.OrdinalIgnoreCase);

            var stack = await _ExecuteAsync(sessionId, ".ecxr; kn 40");
            foreach (var frame in expectedFrames)
                Assert.Contains(frame, stack, StringComparison.Ordinal);
        }
        finally
        {
            await _CloseSessionAsync(sessionId);
        }
    }

    [SkippableFact]
    public async Task SameScenarioTwice_ProducesTwoDumpsWithTheSameFaultingFrames()
    {
        Skip.IfNot(CrashDumpFixture.IsAvailable, CrashDumpFixture.SkipReason);

        var first = await _crashDumps.GetDumpAsync("access-violation", run: 1);
        var second = await _crashDumps.GetDumpAsync("access-violation", run: 2);
        Assert.NotEqual(first, second);

        foreach (var dumpPath in new[] { first, second })
        {
            var sessionId = await _LoadDumpAsync(dumpPath);
            try
            {
                var stack = await _ExecuteAsync(sessionId, ".ecxr; kn 40");
                Assert.Contains("ReadThroughPointer", stack, StringComparison.Ordinal);
                Assert.Contains("ValidateRecord", stack, StringComparison.Ordinal);
            }
            finally
            {
                await _CloseSessionAsync(sessionId);
            }
        }
    }

    [SkippableFact]
    public async Task Deadlock_ShowsBothThreadsAndTheirCriticalSections()
    {
        Skip.IfNot(CrashDumpFixture.IsAvailable, CrashDumpFixture.SkipReason);

        var dumpPath = await _crashDumps.GetDumpAsync("deadlock");
        var sessionId = await _LoadDumpAsync(dumpPath);
        try
        {
            // The dump is taken at the scenario's DebugBreak, not at a fault.
            var exceptionRecord = await _ExecuteAsync(sessionId, ".exr -1");
            Assert.Contains("ExceptionCode: 80000003", exceptionRecord, StringComparison.OrdinalIgnoreCase);

            var stacks = await _ExecuteAsync(sessionId, "~* kn 40");
            Assert.True(_CountOccurrences(stacks, "AcquireInOrder") >= 2,
                $"Expected both deadlocked threads in AcquireInOrder. Stacks:\n{stacks}");

            var locks = await _ExecuteAsync(sessionId, "!locks");
            Assert.True(_CountOccurrences(locks, "CritSec") >= 2,
                $"Expected two owned critical sections. !locks:\n{locks}");
        }
        finally
        {
            await _CloseSessionAsync(sessionId);
        }
    }

    private async Task<string> _LoadDumpAsync(string dumpPath)
    {
        // Our PDB sits next to CrashScenarios.exe
        var symbols = new SymbolsConfiguration(SymbolPathExtra: CrashDumpFixture.ScenarioDirectory);
        var result = await _RunJobAsync("/api/jobs/load-dump", new LoadDumpRequest(dumpPath, symbols), TimeSpan.FromMinutes(20));
        Assert.False(string.IsNullOrWhiteSpace(result));
        return result;
    }

    private Task<string> _ExecuteAsync(string sessionId, string command) =>
        _RunJobAsync("/api/jobs/execute-command", new ExecuteCommandRequest(sessionId, command), TimeSpan.FromMinutes(10));

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

    private static int _CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}
