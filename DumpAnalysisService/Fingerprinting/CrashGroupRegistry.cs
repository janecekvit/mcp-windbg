using System.Text.Json;
using Microsoft.Extensions.Options;
using Shared.Configuration;
using Shared.Models;

namespace DumpAnalysisService.Fingerprinting;

/// <param name="DumpKey">SHA-256 of the dump, or a file identity when it was too large to hash</param>
public sealed record CrashGroupObservation(
    string Fingerprint,
    string Title,
    string ExceptionCode,
    string? Classification,
    string Quality,
    IReadOnlyList<string> Frames,
    string DumpKey,
    DateTimeOffset SeenUtc);

/// <param name="IsNewGroup">The fingerprint was not in the registry before</param>
/// <param name="IsNewDump">The dump had not been counted for this fingerprint before</param>
public sealed record CrashGroupRecordResult(CrashGroup Group, bool IsNewGroup, bool IsNewDump);

public interface ICrashGroupRegistry
{
    /// <summary>Counts the dump under its fingerprint; the same dump is counted once.</summary>
    Task<CrashGroupRecordResult> RecordAsync(CrashGroupObservation observation, CancellationToken cancellationToken = default);

    /// <summary>Known groups, most recently seen first.</summary>
    Task<IReadOnlyList<CrashGroup>> ListAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Crash groups in a small JSON file. Writes go to a temporary file that replaces the registry,
/// so a crash mid-write leaves the previous version; an unreadable file is set aside and the
/// registry starts empty.
/// </summary>
public sealed class CrashGroupRegistry : ICrashGroupRegistry, IDisposable
{
    /// <summary>Dump keys kept per group (the oldest are dropped; Occurrences keeps counting).</summary>
    public const int MaxDumpsPerGroup = 1000;

    private static readonly JsonSerializerOptions _json = new(JsonSerializerOptions.Web) { WriteIndented = true };

    private readonly ILogger<CrashGroupRegistry> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Dictionary<string, CrashGroup>? _groups;

    public CrashGroupRegistry(ILogger<CrashGroupRegistry> logger, IOptions<FingerprintOptions> options)
    {
        _logger = logger;
        var configured = options.Value.RegistryPath;
        RegistryPath = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(ApplicationPaths.GetBaseDirectory(), "fingerprints.json")
            : Environment.ExpandEnvironmentVariables(configured);
    }

    public string RegistryPath { get; }

    public async Task<CrashGroupRecordResult> RecordAsync(CrashGroupObservation observation, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var groups = await _LoadAsync(cancellationToken);
            CrashGroup group;
            bool isNewGroup, isNewDump;
            if (!groups.TryGetValue(observation.Fingerprint, out var existing))
            {
                group = new CrashGroup(observation.Fingerprint, observation.Title, observation.ExceptionCode, observation.Classification,
                    observation.Quality, observation.Frames, observation.SeenUtc, observation.SeenUtc, 1, [observation.DumpKey]);
                isNewGroup = true;
                isNewDump = true;
            }
            else if (existing.Dumps.Contains(observation.DumpKey))
            {
                group = existing;
                isNewGroup = false;
                isNewDump = false;
            }
            else
            {
                var dumps = existing.Dumps.Append(observation.DumpKey).TakeLast(MaxDumpsPerGroup).ToList();
                group = existing with
                {
                    LastSeenUtc = observation.SeenUtc > existing.LastSeenUtc ? observation.SeenUtc : existing.LastSeenUtc,
                    Occurrences = existing.Occurrences + 1,
                    Dumps = dumps,
                    // Keep the frames of the best-symbolised observation.
                    Quality = observation.Quality == CrashFingerprinter.Exact ? CrashFingerprinter.Exact : existing.Quality,
                    Frames = observation.Quality == CrashFingerprinter.Exact && existing.Quality != CrashFingerprinter.Exact ? observation.Frames : existing.Frames,
                };
                isNewGroup = false;
                isNewDump = true;
            }

            if (isNewDump)
            {
                groups[group.Fingerprint] = group;
                await _SaveAsync(groups, cancellationToken);
            }
            return new CrashGroupRecordResult(group, isNewGroup, isNewDump);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<CrashGroup>> ListAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var groups = await _LoadAsync(cancellationToken);
            return groups.Values.OrderByDescending(g => g.LastSeenUtc).ThenBy(g => g.Fingerprint, StringComparer.Ordinal).ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose() => _lock.Dispose();

    private async Task<Dictionary<string, CrashGroup>> _LoadAsync(CancellationToken cancellationToken)
    {
        if (_groups is not null)
            return _groups;

        _groups = new Dictionary<string, CrashGroup>(StringComparer.Ordinal);
        if (!File.Exists(RegistryPath))
            return _groups;

        try
        {
            await using var stream = File.OpenRead(RegistryPath);
            var file = await JsonSerializer.DeserializeAsync<RegistryFile>(stream, _json, cancellationToken);
            foreach (var group in file?.Groups ?? [])
                _groups[group.Fingerprint] = group;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            var aside = $"{RegistryPath}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}";
            _logger.LogWarning(ex, "Crash group registry {Path} is unreadable; moved it to {Aside} and starting empty", RegistryPath, aside);
            File.Move(RegistryPath, aside, overwrite: true);
            _groups.Clear();
        }
        return _groups;
    }

    private async Task _SaveAsync(Dictionary<string, CrashGroup> groups, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(RegistryPath))!);
        var temporary = RegistryPath + ".tmp";
        await using (var stream = File.Create(temporary))
        {
            var file = new RegistryFile(1, groups.Values.OrderBy(g => g.FirstSeenUtc).ThenBy(g => g.Fingerprint, StringComparer.Ordinal).ToList());
            await JsonSerializer.SerializeAsync(stream, file, _json, cancellationToken);
        }
        File.Move(temporary, RegistryPath, overwrite: true);
    }

    private sealed record RegistryFile(int Version, List<CrashGroup> Groups);
}
