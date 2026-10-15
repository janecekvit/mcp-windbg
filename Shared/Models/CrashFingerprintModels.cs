using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Shared.Models;

/// <summary>
/// Result of fingerprint_crash: the crash in a session reduced to a stable fingerprint, and
/// whether that fingerprint was seen before.
/// </summary>
/// <param name="Fingerprint">First 12 hex characters of SHA-256 over the exception code and the normalised frames; null when there is no exception to fingerprint</param>
/// <param name="Quality">"exact" (all frames have symbols), "partial" (some frames by module offset) or "weak" (reported, never grouped)</param>
/// <param name="Title">"&lt;code name&gt; in &lt;top frame&gt;"</param>
/// <param name="Frames">The normalised frames the fingerprint was computed from, top first; recursion shown as "f (×k)"</param>
/// <param name="Recorded">Whether this crash was added to the crash group registry (weak fingerprints are not)</param>
/// <param name="IsNew">True for a fingerprint never seen before; null when not recorded</param>
/// <param name="IsNewDump">False when this very dump (by SHA-256) had already been counted</param>
/// <param name="Occurrences">Distinct dumps with this fingerprint, this one included</param>
/// <param name="Note">Why the fingerprint is weak or was not recorded</param>
public record CrashFingerprintResult(
    string? Fingerprint,
    string Quality,
    string? ExceptionCode,
    string? ExceptionName,
    string? Classification,
    string Title,
    IReadOnlyList<string> Frames,
    bool Recorded,
    bool? IsNew,
    bool? IsNewDump,
    int Occurrences,
    DateTimeOffset? FirstSeenUtc,
    string? Note);

/// <summary>One group of dumps that share a fingerprint.</summary>
/// <param name="Dumps">SHA-256 (or a file identity when the dump was too large to hash) of every dump counted, most recent last</param>
public record CrashGroup(
    string Fingerprint,
    string Title,
    string ExceptionCode,
    string? Classification,
    string Quality,
    IReadOnlyList<string> Frames,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc,
    int Occurrences,
    IReadOnlyList<string> Dumps);

/// <summary>Result of list_crash_groups: known fingerprints, most recently seen first.</summary>
public record CrashGroupList(IReadOnlyList<CrashGroup> Groups);

public record FingerprintCrashRequest(
    [Required(ErrorMessage = "Session ID is required")]
    [property: JsonPropertyName("sessionId")]
    string SessionId);
