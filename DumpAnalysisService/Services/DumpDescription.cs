using DumpAnalysisService.Infrastructure.Minidump;

namespace DumpAnalysisService.Services;

/// <summary>Dump kind and architecture from the dump header (a cheap metadata read, no debugger).</summary>
public static class DumpDescription
{
    private const uint MachineI386 = 0x014c;
    private const uint MachineAmd64 = 0x8664;
    private const uint MachineArm64 = 0xAA64;
    private const uint MachineArmNt = 0x01c4;

    /// <summary>("user-full" | "user-mini" | "kernel", architecture); nulls when the file cannot be parsed.</summary>
    public static (string? Kind, string? Architecture) Read(string dumpPath)
    {
        try
        {
            using var stream = File.OpenRead(dumpPath);
            var data = MinidumpParser.Parse(stream);

            if (data.Kernel is { } kernel)
                return ("kernel", _MachineName(kernel.MachineImageType));

            var architecture = data.SystemInfo is { } sys ? DumpTriageService.ArchitectureName(sys.ProcessorArchitecture) : null;
            return (data.HasFullMemory ? "user-full" : "user-mini", architecture);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return (null, null);
        }
    }

    private static string? _MachineName(uint machine) => machine switch
    {
        MachineAmd64 => "x64",
        MachineI386 => "x86",
        MachineArm64 => "arm64",
        MachineArmNt => "arm",
        _ => null,
    };
}
