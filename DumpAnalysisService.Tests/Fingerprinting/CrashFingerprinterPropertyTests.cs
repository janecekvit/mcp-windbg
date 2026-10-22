using DumpAnalysisService.Fingerprinting;
using FsCheck;
using FsCheck.Xunit;

namespace DumpAnalysisService.Tests.Fingerprinting;

/// <summary>
/// Property tests (FsCheck) for the fingerprinter: it reads debugger output, so any text must parse
/// without throwing, and a fingerprint must not change with what differs between two machines or
/// builds (addresses, offsets, template and argument lists, source lines, module name case).
/// </summary>
public sealed class CrashFingerprinterPropertyTests
{
    private static readonly CrashFingerprinter Fingerprinter = new(FingerprintRules.Load());

    [Property(MaxTest = 500)]
    public void ParseStack_AnyText_YieldsOnlyNonEmptyFrames(NonNull<string> text)
    {
        var frames = CrashFingerprinter.ParseStack("# Child-SP          RetAddr               Call Site\n" + text.Get);

        Assert.All(frames, frame => Assert.False(string.IsNullOrWhiteSpace(frame.Token)));
        Fingerprinter.Compute("0xC0000005", frames);
    }

    [Property(MaxTest = 500)]
    public void NormalizeCallSite_AnyText_DoesNotThrow(NonNull<string> callSite)
    {
        CrashFingerprinter.NormalizeCallSite(callSite.Get);
    }

    [Property(MaxTest = 300)]
    public void Fingerprint_IgnoresAddressesOffsetsTemplatesArgumentsAndModuleCase(
        byte frameCount, uint nameSeed, ulong addressA, ulong addressB, uint offsetA, uint offsetB)
    {
        var count = 1 + frameCount % 8;
        var plain = new List<string>();
        var decorated = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var module = $"app{(nameSeed + (uint)i) % 5}";
            var function = $"Contoso::Worker{i}::Run";
            plain.Add($"{module}!{function}+0x{offsetA + (uint)i:x}");
            decorated.Add($"{module.ToUpperInvariant()}!{function}<int, std::basic_string<char> >(int, char const *)+0x{offsetB + (uint)i:x} [d:\\src\\worker{i}.cpp @ {i + 10}]");
        }

        var first = Fingerprinter.Compute("0xC0000005", CrashFingerprinter.ParseStack(_Listing(plain, addressA)));
        var second = Fingerprinter.Compute("0xc0000005", CrashFingerprinter.ParseStack(_Listing(decorated, addressB)));

        Assert.Equal(first.Frames, second.Frames);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Property(MaxTest = 300)]
    public void Fingerprint_DependsOnTheExceptionCode(uint codeA, uint codeB)
    {
        if (codeA == codeB)
            return;

        var frames = CrashFingerprinter.ParseStack(_Listing(["app!Contoso::Parser::Read+0x1f", "app!Contoso::Main+0x40"], 0x7ff6_1a2b_3c00));

        Assert.NotEqual(
            Fingerprinter.Compute($"0x{codeA:X8}", frames).Fingerprint,
            Fingerprinter.Compute($"0x{codeB:X8}", frames).Fingerprint);
    }

    /// <summary>A cdb kn listing (x64) of the given call sites, with stack and return addresses derived from <paramref name="address"/>.</summary>
    private static string _Listing(IReadOnlyList<string> callSites, ulong address)
    {
        var lines = new List<string> { " # Child-SP          RetAddr               Call Site" };
        for (var i = 0; i < callSites.Count; i++)
        {
            var childSp = address + (ulong)i * 0x40;
            var retAddr = ~address - (ulong)i;
            lines.Add($"{i:x2} {childSp >> 32:x8}`{(uint)childSp:x8} {retAddr >> 32:x8}`{(uint)retAddr:x8}     {callSites[i]}");
        }
        return string.Join("\r\n", lines);
    }
}
