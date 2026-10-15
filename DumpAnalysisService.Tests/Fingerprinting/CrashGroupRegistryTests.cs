using DumpAnalysisService.Fingerprinting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DumpAnalysisService.Tests.Fingerprinting;

public sealed class CrashGroupRegistryTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "crash-groups-" + Guid.NewGuid().ToString("N"));

    private string RegistryPath => Path.Combine(_directory, "nested", "fingerprints.json");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // best-effort cleanup (also when the test never created the directory)
        }
    }

    private CrashGroupRegistry _Registry() =>
        new(NullLogger<CrashGroupRegistry>.Instance, Options.Create(new FingerprintOptions { RegistryPath = RegistryPath }));

    private static CrashGroupObservation _Seen(string fingerprint, string dump, DateTimeOffset at, string quality = CrashFingerprinter.Exact) =>
        new(fingerprint, $"EXCEPTION_ACCESS_VIOLATION in contoso!{fingerprint}", "0xC0000005", "null-pointer", quality,
            [$"contoso!{fingerprint}", "contoso!Main"], dump, at);

    [Fact]
    public async Task FirstObservation_IsANewGroup()
    {
        using var registry = _Registry();

        var result = await registry.RecordAsync(_Seen("aaaaaaaaaaaa", "sha-1", T0));

        Assert.True(result.IsNewGroup);
        Assert.True(result.IsNewDump);
        Assert.Equal(1, result.Group.Occurrences);
        Assert.Equal(T0, result.Group.FirstSeenUtc);
        Assert.True(File.Exists(RegistryPath));
    }

    [Fact]
    public async Task SameDumpTwice_IsCountedOnce()
    {
        using var registry = _Registry();
        await registry.RecordAsync(_Seen("aaaaaaaaaaaa", "sha-1", T0));

        var again = await registry.RecordAsync(_Seen("aaaaaaaaaaaa", "sha-1", T0.AddHours(1)));

        Assert.False(again.IsNewGroup);
        Assert.False(again.IsNewDump);
        Assert.Equal(1, again.Group.Occurrences);
        Assert.Equal(T0, again.Group.LastSeenUtc);
    }

    [Fact]
    public async Task AnotherDumpWithTheSameFingerprint_IncrementsTheGroup()
    {
        using var registry = _Registry();
        await registry.RecordAsync(_Seen("aaaaaaaaaaaa", "sha-1", T0));

        var second = await registry.RecordAsync(_Seen("aaaaaaaaaaaa", "sha-2", T0.AddHours(2)));

        Assert.False(second.IsNewGroup);
        Assert.True(second.IsNewDump);
        Assert.Equal(2, second.Group.Occurrences);
        Assert.Equal(T0, second.Group.FirstSeenUtc);
        Assert.Equal(T0.AddHours(2), second.Group.LastSeenUtc);
        Assert.Equal(["sha-1", "sha-2"], second.Group.Dumps);
    }

    [Fact]
    public async Task Groups_PersistAcrossInstances_AndListNewestFirst()
    {
        using (var registry = _Registry())
        {
            await registry.RecordAsync(_Seen("aaaaaaaaaaaa", "sha-1", T0));
            await registry.RecordAsync(_Seen("bbbbbbbbbbbb", "sha-2", T0.AddHours(1)));
            await registry.RecordAsync(_Seen("aaaaaaaaaaaa", "sha-3", T0.AddHours(3)));
        }

        using var reopened = _Registry();
        var groups = await reopened.ListAsync();

        Assert.Equal(["aaaaaaaaaaaa", "bbbbbbbbbbbb"], groups.Select(g => g.Fingerprint));
        Assert.Equal(2, groups[0].Occurrences);
        Assert.Equal(["contoso!aaaaaaaaaaaa", "contoso!Main"], groups[0].Frames);
    }

    [Fact]
    public async Task ExactObservation_UpgradesAPartialGroup()
    {
        using var registry = _Registry();
        await registry.RecordAsync(_Seen("aaaaaaaaaaaa", "sha-1", T0, CrashFingerprinter.Partial));

        var upgraded = await registry.RecordAsync(_Seen("aaaaaaaaaaaa", "sha-2", T0.AddHours(1)));

        Assert.Equal(CrashFingerprinter.Exact, upgraded.Group.Quality);
    }

    [Fact]
    public async Task CorruptFile_IsSetAside_AndTheRegistryStartsEmpty()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(RegistryPath)!);
        await File.WriteAllTextAsync(RegistryPath, "{ not json");
        using var registry = _Registry();

        Assert.Empty(await registry.ListAsync());
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(RegistryPath)!, "fingerprints.json.corrupt-*"));

        var result = await registry.RecordAsync(_Seen("aaaaaaaaaaaa", "sha-1", T0));
        Assert.True(result.IsNewGroup);
    }

    [Fact]
    public async Task ConcurrentRecords_AreAllCounted()
    {
        using var registry = _Registry();

        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => registry.RecordAsync(_Seen("aaaaaaaaaaaa", $"sha-{i}", T0.AddMinutes(i)))));

        var group = Assert.Single(await registry.ListAsync());
        Assert.Equal(20, group.Occurrences);
        Assert.Equal(20, group.Dumps.Distinct().Count());
    }
}
