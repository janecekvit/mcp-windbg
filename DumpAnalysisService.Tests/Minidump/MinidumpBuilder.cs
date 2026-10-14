using System.Text;

namespace DumpAnalysisService.Tests.Minidump;

/// <summary>
/// Writes small, synthetic minidump files byte by byte following the documented MINIDUMP_*
/// layouts, so the parser can be tested without third-party dump files.
/// </summary>
internal sealed class MinidumpBuilder
{
    public sealed record Module(ulong Base, uint Size, string Path, uint TimeDateStamp = 0x5F000000,
        string? FileVersion = null, string? PdbPath = null, Guid? PdbGuid = null, uint PdbAge = 1);

    public sealed record Exception(uint ThreadId, uint Code, ulong Address, ulong[]? Parameters = null);

    public uint TimeDateStamp { get; set; } = 1_760_000_000;
    public ulong Flags { get; set; } = 0x2; // MiniDumpWithFullMemory
    public ushort Architecture { get; set; } = 9; // AMD64
    public byte Processors { get; set; } = 8;
    public byte ProductType { get; set; } = 1;
    public (uint Major, uint Minor, uint Build) Os { get; set; } = (10, 0, 26100);
    public uint? ProcessId { get; set; } = 4242;
    public uint? ProcessCreateTime { get; set; } = 1_759_999_000;
    public List<uint> ThreadIds { get; } = [100, 200, 300];
    public List<Module> Modules { get; } = [];
    public Exception? Fault { get; set; }
    public bool IncludeSystemInfo { get; set; } = true;

    public byte[] Build()
    {
        var streams = new List<(uint Type, byte[] Data)>();
        var tail = new MemoryStream(); // variable data (strings, CodeView) appended after the streams
        const int headerSize = 32;

        // Lay out stream data first in a scratch list; RVAs are fixed up once the directory size is known.
        if (IncludeSystemInfo)
            streams.Add((7, _SystemInfo()));
        streams.Add((15, _MiscInfo()));
        if (Fault is not null)
            streams.Add((6, _ExceptionStream(Fault)));
        streams.Add((3, _ThreadList()));

        var directorySize = (streams.Count + 1) * 12; // + module list stream
        var moduleListSize = 4 + Modules.Count * 108;
        var offset = headerSize + directorySize;
        var streamOffsets = new List<int>();
        foreach (var (_, data) in streams)
        {
            streamOffsets.Add(offset);
            offset += data.Length;
        }
        var moduleListOffset = offset;
        var tailOffset = moduleListOffset + moduleListSize;

        var moduleList = _ModuleList(tailOffset, tail);

        var output = new MemoryStream();
        var w = new BinaryWriter(output);
        w.Write(0x504D444Du);              // "MDMP"
        w.Write(0x0000A793u);              // version
        w.Write((uint)(streams.Count + 1)); // stream count
        w.Write((uint)headerSize);         // directory RVA
        w.Write(0u);                       // checksum
        w.Write(TimeDateStamp);
        w.Write(Flags);

        for (var i = 0; i < streams.Count; i++)
        {
            w.Write(streams[i].Type);
            w.Write((uint)streams[i].Data.Length);
            w.Write((uint)streamOffsets[i]);
        }
        w.Write(4u); // ModuleListStream
        w.Write((uint)moduleListSize);
        w.Write((uint)moduleListOffset);

        foreach (var (_, data) in streams)
            w.Write(data);
        w.Write(moduleList);
        w.Write(tail.ToArray());
        return output.ToArray();
    }

    private byte[] _SystemInfo()
    {
        var w = _Writer(out var ms);
        w.Write(Architecture);
        w.Write((ushort)6);   // level
        w.Write((ushort)0);   // revision
        w.Write(Processors);
        w.Write(ProductType);
        w.Write(Os.Major);
        w.Write(Os.Minor);
        w.Write(Os.Build);
        w.Write(2u);          // platform id
        w.Write(0u);          // CSD version RVA
        w.Write((ushort)0);   // suite mask
        w.Write((ushort)0);
        w.Write(new byte[24]); // CPU_INFORMATION
        return ms.ToArray();
    }

    private byte[] _MiscInfo()
    {
        var w = _Writer(out var ms);
        var flags = (ProcessId is null ? 0u : 1u) | (ProcessCreateTime is null ? 0u : 2u);
        w.Write(24u);
        w.Write(flags);
        w.Write(ProcessId ?? 0);
        w.Write(ProcessCreateTime ?? 0);
        w.Write(0u);
        w.Write(0u);
        return ms.ToArray();
    }

    private static byte[] _ExceptionStream(Exception fault)
    {
        var w = _Writer(out var ms);
        w.Write(fault.ThreadId);
        w.Write(0u);                 // alignment
        w.Write(fault.Code);
        w.Write(0u);                 // flags
        w.Write(0ul);                // nested record
        w.Write(fault.Address);
        var parameters = fault.Parameters ?? [];
        w.Write((uint)parameters.Length);
        w.Write(0u);
        for (var i = 0; i < 15; i++)
            w.Write(i < parameters.Length ? parameters[i] : 0ul);
        w.Write(0u);                 // context size
        w.Write(0u);                 // context RVA
        return ms.ToArray();
    }

    private byte[] _ThreadList()
    {
        var w = _Writer(out var ms);
        w.Write((uint)ThreadIds.Count);
        foreach (var id in ThreadIds)
        {
            w.Write(id);
            w.Write(new byte[44]);
        }
        return ms.ToArray();
    }

    private byte[] _ModuleList(int tailOffset, MemoryStream tail)
    {
        var tw = new BinaryWriter(tail);
        var w = _Writer(out var ms);
        w.Write((uint)Modules.Count);
        foreach (var module in Modules)
        {
            var nameRva = tailOffset + (int)tail.Length;
            var nameBytes = Encoding.Unicode.GetBytes(module.Path);
            tw.Write((uint)nameBytes.Length);
            tw.Write(nameBytes);
            tw.Write((ushort)0);

            uint cvRva = 0, cvSize = 0;
            if (module.PdbPath is not null)
            {
                cvRva = (uint)(tailOffset + tail.Length);
                var pdbBytes = Encoding.UTF8.GetBytes(module.PdbPath);
                tw.Write(0x53445352u); // "RSDS"
                tw.Write((module.PdbGuid ?? Guid.Empty).ToByteArray());
                tw.Write(module.PdbAge);
                tw.Write(pdbBytes);
                tw.Write((byte)0);
                cvSize = (uint)(24 + pdbBytes.Length + 1);
            }

            w.Write(module.Base);
            w.Write(module.Size);
            w.Write(0u);                     // checksum
            w.Write(module.TimeDateStamp);
            w.Write((uint)nameRva);
            // VS_FIXEDFILEINFO
            if (module.FileVersion is not null)
            {
                var v = module.FileVersion.Split('.').Select(uint.Parse).ToArray();
                w.Write(0xFEEF04BDu);
                w.Write(0x00010000u);
                w.Write((v[0] << 16) | v[1]);
                w.Write((v[2] << 16) | v[3]);
                w.Write(new byte[36]);
            }
            else
            {
                w.Write(new byte[52]);
            }
            w.Write(cvSize);
            w.Write(cvRva);
            w.Write(new byte[8]);  // misc record
            w.Write(new byte[16]); // reserved
        }
        return ms.ToArray();
    }

    private static BinaryWriter _Writer(out MemoryStream stream)
    {
        stream = new MemoryStream();
        return new BinaryWriter(stream);
    }

    /// <summary>A 64-bit kernel dump header ("PAGEDU64") with a bug check.</summary>
    public static byte[] KernelDump64(uint bugCheckCode, params ulong[] parameters)
    {
        var bytes = new byte[4096];
        var w = new BinaryWriter(new MemoryStream(bytes));
        w.Write(0x45474150u); // "PAGE"
        w.Write(0x34365544u); // "DU64"
        w.Seek(48, SeekOrigin.Begin);
        w.Write(0x8664u);     // AMD64
        w.Write(16u);         // processors
        w.Write(bugCheckCode);
        w.Seek(64, SeekOrigin.Begin);
        for (var i = 0; i < 4; i++)
            w.Write(i < parameters.Length ? parameters[i] : 0ul);
        return bytes;
    }
}
