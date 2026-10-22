using System.Text;
using DumpAnalysisService.Infrastructure.Minidump;
using DumpAnalysisService.Services;
using FsCheck.Xunit;

namespace DumpAnalysisService.Tests.Minidump;

/// <summary>
/// Fuzzes the minidump parser with FsCheck: dump files come from users and may be truncated,
/// corrupt or crafted. Whatever the bytes, parsing either succeeds (and triage can be built from
/// the result) or fails with <see cref="InvalidDataException"/>; any other exception is a bug.
/// </summary>
public sealed class MinidumpParserPropertyTests
{
    private static readonly byte[][] Seeds = [_RichAccessViolation(), _CppExceptionX86(), _DelayLoad()];

    [Property(MaxTest = 500)]
    public void MutatedDump_ParsesOrIsRejected(byte seedIndex, int[] positions, byte[] values)
    {
        var bytes = (byte[])Seeds[seedIndex % Seeds.Length].Clone();
        foreach (var (position, value) in (positions ?? []).Zip(values ?? []))
            bytes[(int)((uint)position % (uint)bytes.Length)] = value;

        _ParseOrReject(bytes);
    }

    [Property(MaxTest = 300)]
    public void TruncatedDump_ParsesOrIsRejected(byte seedIndex, int length)
    {
        var seed = Seeds[seedIndex % Seeds.Length];
        _ParseOrReject(seed[..(int)((uint)length % (uint)(seed.Length + 1))]);
    }

    [Property(MaxTest = 300)]
    public void RandomBytesAfterSignature_ParseOrAreRejected(bool kernel, byte[] tail)
    {
        var signature = kernel ? "PAGEDU64"u8.ToArray() : "MDMP"u8.ToArray();
        _ParseOrReject([.. signature, .. tail ?? []]);
    }

    private static void _ParseOrReject(byte[] bytes)
    {
        MinidumpData data;
        try
        {
            data = MinidumpParser.Parse(new MemoryStream(bytes));
        }
        catch (InvalidDataException)
        {
            return;
        }

        DumpTriageService.Build("fuzz.dmp", bytes.Length, sha256: null, data);
    }

    private static byte[] _RichAccessViolation()
    {
        var builder = new MinidumpBuilder
        {
            Fault = new MinidumpBuilder.Exception(200, ExceptionCodes.AccessViolation, 0x7ff6_0000_1234, [0, 0x10]),
            Registers = new() { ["rip"] = 0x7ff6_0000_1234, ["rsp"] = 0x0000_00c3_77cf_f6b8, ["rcx"] = 0x10 },
            HandleTypes = ["File", "Event", "Thread", "Key"],
            ProcessVmCounters = new(0x4000_0000, 0x5000_0000, 0x2000_0000, 0x3000_0000, 0x1_0000_0000),
            SystemMemoryInfo = new(0x10_0000, 0x40_0000, 0x40_0000),
            Comment = "fuzz seed",
        };
        builder.Modules.Add(new(0x7ff6_0000_0000, 0x20000, @"C:\Apps\Contoso\contoso.exe", FileVersion: "2.5.0.17",
            PdbPath: @"D:\build\obj\contoso.pdb", PdbGuid: Guid.Parse("0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0"), PdbAge: 3));
        builder.Modules.Add(new(0x7ffa_0000_0000, 0x1F0000, @"C:\Windows\SYSTEM32\ntdll.dll", FileVersion: "10.0.26100.1"));
        builder.UnloadedModules.Add(new(0x7ff7_0000_0000, 0x10000, @"C:\Apps\Contoso\plugin.dll"));
        builder.ThreadInfos.Add(new(200, DateTimeOffset.FromUnixTimeSeconds(1_759_999_100), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)));
        builder.ThreadNames[200] = "worker";
        builder.MemoryRegions.Add(new(0x7ff6_0000_0000, 0x20000, 0x1000, 0x20, 0x100_0000));
        builder.MemoryRegions.Add(new(0x7ff6_0002_0000, 0x10000, 0x10000));
        builder.Memory.Add(new(0x7ff6_0000_1000, new byte[0x80]));
        return builder.Build();
    }

    private static byte[] _CppExceptionX86()
    {
        const ulong throwInfo = 0x0040_3000, array = 0x0040_3010, type = 0x0040_3020, descriptor = 0x0040_3100;
        var image = new byte[0x200];
        void Put32(ulong at, uint value) => BitConverter.GetBytes(value).CopyTo(image, (int)(at - throwInfo));
        Put32(throwInfo + 12, (uint)array);
        Put32(array, 1);
        Put32(array + 4, (uint)type);
        Put32(type + 4, (uint)descriptor);
        Encoding.ASCII.GetBytes(".?AVParseError@Contoso@@\0").CopyTo(image, (int)(descriptor + 8 - throwInfo));

        var builder = new MinidumpBuilder
        {
            Architecture = 0,
            Fault = new MinidumpBuilder.Exception(200, ExceptionCodes.CppException, 0x7700_1000, [0x19930520, 0x0100_0000, throwInfo]),
        };
        builder.Modules.Add(new(0x0040_0000, 0x10000, @"C:\Apps\Contoso\contoso32.exe"));
        builder.Memory.Add(new(throwInfo, image));
        return builder.Build();
    }

    private static byte[] _DelayLoad()
    {
        const ulong info = 0x0000_0200_0000_0000;
        var block = new byte[0x200];
        BitConverter.GetBytes(info + 0x100).CopyTo(block, 24);
        BitConverter.GetBytes(1u).CopyTo(block, 32);
        BitConverter.GetBytes(info + 0x140).CopyTo(block, 40);
        BitConverter.GetBytes(127u).CopyTo(block, 64);
        Encoding.ASCII.GetBytes("contoso-crypto.dll\0").CopyTo(block, 0x100);
        Encoding.ASCII.GetBytes("ContosoSign\0").CopyTo(block, 0x140);

        var builder = new MinidumpBuilder
        {
            Fault = new MinidumpBuilder.Exception(200, ExceptionCodes.DelayLoadProcNotFound, 0x7ffa_1000_2000, [info]),
        };
        builder.Modules.Add(new(0x7ffa_1000_0000, 0x90000, @"C:\Windows\System32\KERNELBASE.dll"));
        builder.Memory.Add(new(info, block));
        return builder.Build();
    }
}
