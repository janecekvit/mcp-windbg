using DumpAnalysisService.Fingerprinting;
using DumpAnalysisService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Shared.Models;

namespace DumpAnalysisService.Tests.Fingerprinting;

public sealed class CrashFingerprintServiceTests : IDisposable
{
    private const string SessionId = "abc12345";
    private const string DumpPath = @"C:\dumps\app.dmp";

    private const string Stack = """
         # Child-SP          RetAddr               Call Site
        00 000000c3`77cff6b8 00007ff6`1a2b3c61     contoso!Parser::ReadField+0x4
        01 000000c3`77cff6c0 00007ff6`1a2b3c8a     contoso!Parser::Parse+0x11
        02 000000c3`77cff6f0 00007ff6`1a2b3cb3     contoso!main+0xa
        03 000000c3`77cff7d0 00007ffa`124dc34c     KERNEL32!BaseThreadInitThunk+0x17
        """;

    private readonly string _registryPath = Path.Combine(Path.GetTempPath(), $"fingerprints-{Guid.NewGuid():N}.json");
    private readonly Mock<ISessionManagerService> _sessions = new();
    private readonly Mock<IDumpTriageService> _triage = new();
    private readonly CrashGroupRegistry _registry;

    public CrashFingerprintServiceTests()
    {
        _registry = new CrashGroupRegistry(NullLogger<CrashGroupRegistry>.Instance, Options.Create(new FingerprintOptions { RegistryPath = _registryPath }));
        _sessions.Setup(s => s.GetDumpFile(SessionId)).Returns(DumpPath);
        _sessions.Setup(s => s.ExecuteInternalCommandAsync(It.IsAny<string>(), SessionId, CrashFingerprintService.StackCommand, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Stack);
    }

    public void Dispose()
    {
        _registry.Dispose();
        File.Delete(_registryPath);
    }

    private CrashFingerprintService _Service() => new(_sessions.Object, _triage.Object, new Mock<IJobManagerService>().Object,
        new CrashFingerprinter(FingerprintRules.Load()), _registry, NullLogger<CrashFingerprintService>.Instance);

    private void _Triage(string? code, string classification = "null-pointer", string? sha = "sha-1")
    {
        var fault = code is null
            ? null
            : new DumpFaultInfo(code, "EXCEPTION_ACCESS_VIOLATION", classification, 1, null, "0x7ff61a2b3c44", "contoso.exe", "0x3c44",
                null, "read", "0x0", null, null, [], new Dictionary<string, string>(), [], null, null);
        var result = new DumpTriageResult(
            new DumpFileInfo(DumpPath, 1, "user-minidump", sha, true, null, [], null),
            null, null, fault, null, null, null, null, null, null, null, []);
        _triage.Setup(t => t.TriageAsync(DumpPath, null, It.IsAny<CancellationToken>())).ReturnsAsync(result);
    }

    [Fact]
    public async Task Crash_IsFingerprinted_AndRecordedOncePerDump()
    {
        _Triage("0xC0000005");
        var service = _Service();

        var first = await service.FingerprintAsync("job-1", SessionId);
        var again = await service.FingerprintAsync("job-2", SessionId);

        Assert.Equal(CrashFingerprinter.Exact, first.Quality);
        Assert.Equal(["contoso!Parser::ReadField", "contoso!Parser::Parse", "contoso!main"], first.Frames);
        Assert.Equal("EXCEPTION_ACCESS_VIOLATION in contoso!Parser::ReadField", first.Title);
        Assert.True(first.Recorded);
        Assert.True(first.IsNew);
        Assert.Equal(1, first.Occurrences);

        Assert.Equal(first.Fingerprint, again.Fingerprint);
        Assert.False(again.IsNew);
        Assert.False(again.IsNewDump);
        Assert.Equal(1, again.Occurrences);
        Assert.Contains("already counted", again.Note);

        var group = Assert.Single((await service.ListGroupsAsync()).Groups);
        Assert.Equal(first.Fingerprint, group.Fingerprint);
        Assert.Equal(["sha-1"], group.Dumps);
    }

    [Fact]
    public async Task Snapshot_HasNoFingerprint_AndDoesNotRunCdb()
    {
        _Triage(null);

        var result = await _Service().FingerprintAsync("job-1", SessionId);

        Assert.Null(result.Fingerprint);
        Assert.False(result.Recorded);
        Assert.Contains("no exception record", result.Note);
        _sessions.Verify(s => s.ExecuteInternalCommandAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BreakpointDump_IsReportedButNotGrouped()
    {
        _Triage("0x80000003", "breakpoint");

        var result = await _Service().FingerprintAsync("job-1", SessionId);

        Assert.NotNull(result.Fingerprint);
        Assert.Equal(CrashFingerprinter.Weak, result.Quality);
        Assert.False(result.Recorded);
        Assert.Null(result.IsNew);
        Assert.Empty((await _Service().ListGroupsAsync()).Groups);
    }

    [Fact]
    public async Task WeakStack_IsReportedButNotGrouped()
    {
        _Triage("0xC0000005");
        _sessions.Setup(s => s.ExecuteInternalCommandAsync(It.IsAny<string>(), SessionId, CrashFingerprintService.StackCommand, It.IsAny<CancellationToken>()))
            .ReturnsAsync(" # Child-SP          RetAddr               Call Site\n00 000000c3`77cff6b8 00007ff6`1a2b3c61     contoso+0x1a2b\n");

        var result = await _Service().FingerprintAsync("job-1", SessionId);

        Assert.Equal(CrashFingerprinter.Weak, result.Quality);
        Assert.False(result.Recorded);
        Assert.Contains("Weak fingerprint", result.Note);
    }

    [Fact]
    public async Task DumpTooLargeToHash_IsIdentifiedByFile()
    {
        _Triage("0xC0000005", sha: null);

        await _Service().FingerprintAsync("job-1", SessionId);

        var group = Assert.Single((await _Service().ListGroupsAsync()).Groups);
        Assert.StartsWith("file:", Assert.Single(group.Dumps));
    }

    [Fact]
    public async Task UnknownSession_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _Service().FingerprintAsync("job-1", "missing"));
    }
}
