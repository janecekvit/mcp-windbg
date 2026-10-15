using System.Security.Cryptography;
using System.Text;
using DumpAnalysisService.Services;
using Shared.Models;

namespace DumpAnalysisService.Fingerprinting;

public interface ICrashFingerprintService
{
    /// <exception cref="ArgumentException">The session does not exist.</exception>
    Task<CrashFingerprintResult> FingerprintAsync(string jobId, string sessionId, CancellationToken cancellationToken = default);

    Task<CrashGroupList> ListGroupsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// fingerprint_crash: the exception code (and the dump's SHA-256) come from triage of the
/// session's dump, the faulting stack from cdb (<c>.ecxr; kn</c>); <see cref="CrashFingerprinter"/>
/// reduces it and <see cref="ICrashGroupRegistry"/> counts it. Weak fingerprints and hang dumps
/// are reported but never grouped.
/// </summary>
public sealed class CrashFingerprintService : ICrashFingerprintService
{
    /// <summary>Frames requested from cdb; deep recursion is collapsed, so this only bounds the output.</summary>
    public const string StackCommand = ".ecxr;kn 100";

    private readonly ISessionManagerService _sessions;
    private readonly IDumpTriageService _triage;
    private readonly IJobManagerService _jobs;
    private readonly CrashFingerprinter _fingerprinter;
    private readonly ICrashGroupRegistry _registry;
    private readonly ILogger<CrashFingerprintService> _logger;

    public CrashFingerprintService(
        ISessionManagerService sessions,
        IDumpTriageService triage,
        IJobManagerService jobs,
        CrashFingerprinter fingerprinter,
        ICrashGroupRegistry registry,
        ILogger<CrashFingerprintService> logger)
    {
        _sessions = sessions;
        _triage = triage;
        _jobs = jobs;
        _fingerprinter = fingerprinter;
        _registry = registry;
        _logger = logger;
    }

    public async Task<CrashFingerprintResult> FingerprintAsync(string jobId, string sessionId, CancellationToken cancellationToken = default)
    {
        var dumpFile = _sessions.GetDumpFile(sessionId)
            ?? throw new ArgumentException($"Session {sessionId} not found", nameof(sessionId));

        await _jobs.UpdateProgressAsync(jobId, JobPhase.Analyzing, 0.1, "Reading the exception record");
        var triage = await _triage.TriageAsync(dumpFile, cancellationToken: cancellationToken);
        if (triage.Fault is not { } fault)
        {
            return new CrashFingerprintResult(null, CrashFingerprinter.Weak, null, null, null, "No exception", [], Recorded: false,
                IsNew: null, IsNewDump: null, Occurrences: 0, FirstSeenUtc: null,
                Note: "The dump has no exception record (a snapshot or hang dump). Hang fingerprints need waiter-chain analysis and are not supported yet.");
        }

        await _jobs.UpdateProgressAsync(jobId, JobPhase.Analyzing, 0.4, "Reading the faulting stack");
        var stack = await _sessions.ExecuteInternalCommandAsync(jobId, sessionId, StackCommand, cancellationToken);
        var computed = _fingerprinter.Compute(fault.Code, CrashFingerprinter.ParseStack(stack));
        var title = $"{fault.CodeName} in {computed.Frames.FirstOrDefault(f => f != CrashFingerprinter.UnknownFrame) ?? fault.Module ?? fault.Address}";

        string? note = null;
        var quality = computed.Quality;
        if (fault.Classification == "breakpoint")
        {
            quality = CrashFingerprinter.Weak;
            note = "A break instruction (hang or on-demand dump): reported but not grouped; hang fingerprints are not supported yet.";
        }
        else if (quality == CrashFingerprinter.Weak)
        {
            note = "Weak fingerprint (no symbols, or fewer than two meaningful frames): reported but not grouped. Load the module's PDBs and fingerprint again.";
        }

        if (quality == CrashFingerprinter.Weak)
        {
            _logger.LogInformation("Weak fingerprint {Fingerprint} for session {SessionId}: {Title}", computed.Fingerprint, sessionId, title);
            return new CrashFingerprintResult(computed.Fingerprint, quality, fault.Code, fault.CodeName, fault.Classification, title, computed.Frames,
                Recorded: false, IsNew: null, IsNewDump: null, Occurrences: 0, FirstSeenUtc: null, Note: note);
        }

        await _jobs.UpdateProgressAsync(jobId, JobPhase.Analyzing, 0.8, "Recording the crash group");
        var recorded = await _registry.RecordAsync(new CrashGroupObservation(
            computed.Fingerprint, title, fault.Code, fault.Classification, quality, computed.Frames,
            triage.Dump.Sha256 ?? _FileIdentity(dumpFile), DateTimeOffset.UtcNow), cancellationToken);

        _logger.LogInformation("Fingerprint {Fingerprint} ({Quality}) for session {SessionId}: {Title}, {Occurrences} occurrence(s)",
            computed.Fingerprint, quality, sessionId, title, recorded.Group.Occurrences);

        return new CrashFingerprintResult(computed.Fingerprint, quality, fault.Code, fault.CodeName, fault.Classification, title, computed.Frames,
            Recorded: true, IsNew: recorded.IsNewGroup, IsNewDump: recorded.IsNewDump, Occurrences: recorded.Group.Occurrences,
            FirstSeenUtc: recorded.Group.FirstSeenUtc,
            Note: recorded.IsNewDump ? null : "This dump was already counted for this fingerprint.");
    }

    public async Task<CrashGroupList> ListGroupsAsync(CancellationToken cancellationToken = default) =>
        new(await _registry.ListAsync(cancellationToken));

    /// <summary>For dumps too large to hash: full path, size and last write time identify the file.</summary>
    private static string _FileIdentity(string path)
    {
        var info = new FileInfo(path);
        var identity = info.Exists
            ? $"{Path.GetFullPath(path).ToUpperInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}"
            : Path.GetFullPath(path).ToUpperInvariant();
        return "file:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }
}
