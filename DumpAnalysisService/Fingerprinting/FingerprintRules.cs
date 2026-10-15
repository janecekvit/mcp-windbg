using System.Text.Json;
using System.Text.RegularExpressions;

namespace DumpAnalysisService.Fingerprinting;

/// <summary>
/// Configuration bound from the "Fingerprint" section.
/// </summary>
public sealed class FingerprintOptions
{
    public const string SectionName = "Fingerprint";

    /// <summary>Crash group registry file; default %LOCALAPPDATA%\DumpAnalysisService\fingerprints.json.</summary>
    public string? RegistryPath { get; set; }

    /// <summary>A rules file that replaces the built-in fingerprint-rules.json.</summary>
    public string? RulesPath { get; set; }

    /// <summary>Overrides the rules' frame count (meaningful frames hashed).</summary>
    public int? FrameCount { get; set; }
}

/// <summary>
/// Which frames are noise shared by every crash, and how many meaningful frames to hash.
/// Patterns are "module!function" globs ('*' matches anything), case-insensitive; a frame
/// without a symbol is matched as "module!".
/// </summary>
public sealed class FingerprintRules
{
    public const int DefaultFrameCount = 5;
    private const string ResourceName = "fingerprint-rules.json";

    private readonly Regex[] _ignore;

    public FingerprintRules(int frameCount, IEnumerable<string> ignoreFrames)
    {
        if (frameCount is < 1 or > 64)
            throw new ArgumentOutOfRangeException(nameof(frameCount), frameCount, "Frame count must be between 1 and 64");
        FrameCount = frameCount;
        IgnoreFrames = ignoreFrames.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        _ignore = IgnoreFrames
            .Select(p => new Regex("^" + Regex.Escape(p.Trim()).Replace(@"\*", ".*") + "$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled))
            .ToArray();
    }

    public int FrameCount { get; }

    public IReadOnlyList<string> IgnoreFrames { get; }

    public bool IsIgnored(StackFrame frame)
    {
        var key = frame.Kind == FrameKind.Symbol ? frame.Token : frame.Module + "!";
        return frame.Kind != FrameKind.Unknown && _ignore.Any(r => r.IsMatch(key));
    }

    /// <summary>The built-in rules, or <see cref="FingerprintOptions.RulesPath"/> when set.</summary>
    public static FingerprintRules Load(FingerprintOptions? options = null)
    {
        var json = string.IsNullOrWhiteSpace(options?.RulesPath)
            ? _ReadResource()
            : File.ReadAllText(options.RulesPath);
        var file = JsonSerializer.Deserialize<RulesFile>(json, JsonSerializerOptions.Web)
            ?? throw new InvalidDataException("Fingerprint rules file is empty");
        return new FingerprintRules(options?.FrameCount ?? file.FrameCount ?? DefaultFrameCount, file.IgnoreFrames ?? []);
    }

    private static string _ReadResource()
    {
        using var stream = typeof(FingerprintRules).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is missing");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed record RulesFile(int? FrameCount, string[]? IgnoreFrames);
}
