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

    public sealed record UnloadedModule(ulong Base, uint Size, string Path, uint TimeDateStamp = 0x5F000000);

    public sealed record ThreadInfo(uint ThreadId, DateTimeOffset CreateTime, TimeSpan UserTime, TimeSpan KernelTime,
        ulong StartAddress = 0, uint DumpFlags = 0);

    /// <param name="State">0x1000 commit, 0x2000 reserve, 0x10000 free</param>
    public sealed record MemoryRegion(ulong Base, ulong Size, uint State, uint Protect = 0, uint Type = 0);

    public sealed record VmCounters(ulong PrivateUsage, ulong PeakPrivateUsage, ulong WorkingSet, ulong PeakWorkingSet,
        ulong? VirtualSize = null, ulong? JobPrivateCommitLimit = null);

    public sealed record SystemMemory(ulong CommittedPages, ulong CommitLimitPages, uint PhysicalPages, uint PageSize = 4096);

    public sealed record MemoryBlock(ulong Address, byte[] Data);

    public uint TimeDateStamp { get; set; } = 1_760_000_000;
    public ulong Flags { get; set; } = 0x2; // MiniDumpWithFullMemory
    public ushort Architecture { get; set; } = 9; // AMD64
    public byte Processors { get; set; } = 8;
    public byte ProductType { get; set; } = 1;
    public (uint Major, uint Minor, uint Build) Os { get; set; } = (10, 0, 26100);
    public uint? ProcessId { get; set; } = 4242;
    public uint? ProcessCreateTime { get; set; } = 1_759_999_000;
    public (uint User, uint Kernel)? ProcessTimes { get; set; }
    public List<uint> ThreadIds { get; } = [100, 200, 300];
    public List<Module> Modules { get; } = [];
    public Exception? Fault { get; set; }
    public bool IncludeSystemInfo { get; set; } = true;

    /// <summary>Faulting thread registers written into an x64 or x86 CONTEXT (by <see cref="Architecture"/>).</summary>
    public Dictionary<string, ulong>? Registers { get; set; }

    public List<UnloadedModule> UnloadedModules { get; } = [];
    public List<ThreadInfo> ThreadInfos { get; } = [];
    public Dictionary<uint, string> ThreadNames { get; } = [];
    public List<MemoryRegion> MemoryRegions { get; } = [];

    /// <summary>One entry per open handle: its object type name. Null: no handle data stream.</summary>
    public List<string>? HandleTypes { get; set; }

    public VmCounters? ProcessVmCounters { get; set; }
    public SystemMemory? SystemMemoryInfo { get; set; }
    public string? Comment { get; set; }

    /// <summary>Captured memory, written as a Memory64ListStream.</summary>
    public List<MemoryBlock> Memory { get; } = [];

    private static readonly (string Name, int Offset)[] _x64Registers =
    [
        ("rax", 0x78), ("rcx", 0x80), ("rdx", 0x88), ("rbx", 0x90), ("rsp", 0x98), ("rbp", 0xA0), ("rsi", 0xA8), ("rdi", 0xB0),
        ("r8", 0xB8), ("r9", 0xC0), ("r10", 0xC8), ("r11", 0xD0), ("r12", 0xD8), ("r13", 0xE0), ("r14", 0xE8), ("r15", 0xF0), ("rip", 0xF8),
    ];

    private static readonly (string Name, int Offset)[] _x86Registers =
    [
        ("edi", 0x9C), ("esi", 0xA0), ("ebx", 0xA4), ("edx", 0xA8), ("ecx", 0xAC), ("eax", 0xB0), ("ebp", 0xB4), ("eip", 0xB8), ("esp", 0xC4),
    ];

    public byte[] Build()
    {
        // Each stream is produced by a function that may append variable data (strings, records) first.
        var streams = new List<(uint Type, Func<Appender, byte[]> Data)>();
        if (IncludeSystemInfo)
            streams.Add((7, _ => _SystemInfo()));
        streams.Add((15, _ => _MiscInfo()));
        if (Fault is not null)
            streams.Add((6, _ExceptionStream));
        if (UnloadedModules.Count > 0)
            streams.Add((14, _UnloadedModuleList));
        if (ThreadInfos.Count > 0)
            streams.Add((17, _ => _ThreadInfoList()));
        if (ThreadNames.Count > 0)
            streams.Add((24, _ThreadNameList));
        if (MemoryRegions.Count > 0)
            streams.Add((16, _ => _MemoryInfoList()));
        if (HandleTypes is not null)
            streams.Add((12, _HandleData));
        if (ProcessVmCounters is not null)
            streams.Add((22, _ => _VmCounters(ProcessVmCounters)));
        if (SystemMemoryInfo is not null)
            streams.Add((21, _ => _SystemMemory(SystemMemoryInfo)));
        if (Comment is not null)
            streams.Add((11, _ => Encoding.Unicode.GetBytes(Comment + "\0")));
        if (Memory.Count > 0)
            streams.Add((9, _Memory64List));
        streams.Add((3, _ => _ThreadList()));
        streams.Add((4, _ModuleList)); // last: tests corrupt the module count through the last directory entry

        const int headerSize = 32;
        var appender = new Appender(headerSize + streams.Count * 12);
        var directory = new List<(uint Type, uint Size, uint Rva)>();
        foreach (var (type, factory) in streams)
        {
            var data = factory(appender);
            directory.Add((type, (uint)data.Length, appender.Append(data)));
        }

        var output = appender.ToArray();
        var w = new BinaryWriter(new MemoryStream(output));
        w.Write(0x504D444Du);              // "MDMP"
        w.Write(0x0000A793u);              // version
        w.Write((uint)streams.Count);      // stream count
        w.Write((uint)headerSize);         // directory RVA
        w.Write(0u);                       // checksum
        w.Write(TimeDateStamp);
        w.Write(Flags);
        foreach (var (type, size, rva) in directory)
        {
            w.Write(type);
            w.Write(size);
            w.Write(rva);
        }
        return output;
    }

    /// <summary>Appends blobs after the header and directory and hands out their RVAs.</summary>
    private sealed class Appender
    {
        private readonly MemoryStream _stream = new();

        public Appender(int reserved) => _stream.Write(new byte[reserved]);

        public uint Append(byte[] data)
        {
            while (_stream.Length % 4 != 0)
                _stream.WriteByte(0);
            var rva = (uint)_stream.Length;
            _stream.Write(data);
            return rva;
        }

        /// <summary>MINIDUMP_STRING: byte length, UTF-16LE text, terminating NUL.</summary>
        public uint AppendString(string text)
        {
            var bytes = Encoding.Unicode.GetBytes(text);
            var w = _Writer(out var ms);
            w.Write((uint)bytes.Length);
            w.Write(bytes);
            w.Write((ushort)0);
            return Append(ms.ToArray());
        }

        public byte[] ToArray() => _stream.ToArray();
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
        var flags = (ProcessId is null ? 0u : 1u) | (ProcessCreateTime is null && ProcessTimes is null ? 0u : 2u);
        w.Write(24u);
        w.Write(flags);
        w.Write(ProcessId ?? 0);
        w.Write(ProcessCreateTime ?? 0);
        w.Write(ProcessTimes?.User ?? 0);
        w.Write(ProcessTimes?.Kernel ?? 0);
        return ms.ToArray();
    }

    private byte[] _ExceptionStream(Appender appender)
    {
        uint contextSize = 0, contextRva = 0;
        if (Registers is not null)
        {
            var is64 = Architecture == 9;
            var context = new byte[is64 ? 0x4D0 : 0x2CC];
            BitConverter.GetBytes(is64 ? 0x10001Fu : 0x1003Fu).CopyTo(context, is64 ? 0x30 : 0);
            foreach (var (name, offset) in is64 ? _x64Registers : _x86Registers)
            {
                if (!Registers.TryGetValue(name, out var value))
                    continue;
                if (is64)
                    BitConverter.GetBytes(value).CopyTo(context, offset);
                else
                    BitConverter.GetBytes((uint)value).CopyTo(context, offset);
            }
            contextSize = (uint)context.Length;
            contextRva = appender.Append(context);
        }

        var fault = Fault!;
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
        w.Write(contextSize);
        w.Write(contextRva);
        return ms.ToArray();
    }

    private byte[] _UnloadedModuleList(Appender appender)
    {
        var w = _Writer(out var ms);
        w.Write(12u);  // SizeOfHeader
        w.Write(24u);  // SizeOfEntry
        w.Write((uint)UnloadedModules.Count);
        foreach (var module in UnloadedModules)
        {
            w.Write(module.Base);
            w.Write(module.Size);
            w.Write(0u); // checksum
            w.Write(module.TimeDateStamp);
            w.Write(appender.AppendString(module.Path));
        }
        return ms.ToArray();
    }

    private byte[] _ThreadInfoList()
    {
        var w = _Writer(out var ms);
        w.Write(12u);  // SizeOfHeader
        w.Write(64u);  // SizeOfEntry
        w.Write((uint)ThreadInfos.Count);
        foreach (var t in ThreadInfos)
        {
            w.Write(t.ThreadId);
            w.Write(t.DumpFlags);
            w.Write(0u);                       // DumpError
            w.Write(0u);                       // ExitStatus
            w.Write(t.CreateTime.UtcDateTime.ToFileTimeUtc());
            w.Write(0L);                       // ExitTime
            w.Write(t.KernelTime.Ticks);
            w.Write(t.UserTime.Ticks);
            w.Write(t.StartAddress);
            w.Write(0xFFul);                   // Affinity
        }
        return ms.ToArray();
    }

    private byte[] _ThreadNameList(Appender appender)
    {
        var w = _Writer(out var ms);
        w.Write((uint)ThreadNames.Count);
        foreach (var (id, name) in ThreadNames)
        {
            w.Write(id);
            w.Write((ulong)appender.AppendString(name)); // RVA64
        }
        return ms.ToArray();
    }

    private byte[] _MemoryInfoList()
    {
        var w = _Writer(out var ms);
        w.Write(16u);  // SizeOfHeader
        w.Write(48u);  // SizeOfEntry
        w.Write((ulong)MemoryRegions.Count);
        foreach (var r in MemoryRegions)
        {
            w.Write(r.Base);
            w.Write(r.State == 0x10000 ? 0ul : r.Base); // AllocationBase
            w.Write(r.Protect);                          // AllocationProtect
            w.Write(0u);
            w.Write(r.Size);
            w.Write(r.State);
            w.Write(r.Protect);
            w.Write(r.Type);
            w.Write(0u);
        }
        return ms.ToArray();
    }

    private byte[] _HandleData(Appender appender)
    {
        var typeRvas = HandleTypes!.Distinct().ToDictionary(t => t, appender.AppendString);
        var w = _Writer(out var ms);
        w.Write(16u);  // SizeOfHeader
        w.Write(40u);  // SizeOfDescriptor (MINIDUMP_HANDLE_DESCRIPTOR_2)
        w.Write((uint)HandleTypes!.Count);
        w.Write(0u);
        for (var i = 0; i < HandleTypes.Count; i++)
        {
            w.Write((ulong)(4 + i * 4));       // handle value
            w.Write(typeRvas[HandleTypes[i]]); // TypeNameRva
            w.Write(0u);                       // ObjectNameRva
            w.Write(0u);                       // Attributes
            w.Write(0x1F0003u);                // GrantedAccess
            w.Write(1u);                       // HandleCount
            w.Write(1u);                       // PointerCount
            w.Write(0u);                       // ObjectInfoRva
            w.Write(0u);                       // Reserved0
        }
        return ms.ToArray();
    }

    private static byte[] _VmCounters(VmCounters vm)
    {
        // MINIDUMP_PROCESS_VM_COUNTERS_2 (152 bytes)
        var flags = (ushort)(0x1 | 0x4 | (vm.VirtualSize is null ? 0 : 0x2) | (vm.JobPrivateCommitLimit is null ? 0 : 0x10));
        var bytes = new byte[152];
        var w = new BinaryWriter(new MemoryStream(bytes));
        w.Write((ushort)2);
        w.Write(flags);
        w.Write(1234u);               // PageFaultCount
        w.Write(vm.PeakWorkingSet);   // @8
        w.Write(vm.WorkingSet);       // @16
        w.Seek(56, SeekOrigin.Begin);
        w.Write(vm.PrivateUsage);     // PagefileUsage @56
        w.Write(vm.PeakPrivateUsage); // PeakPagefileUsage @64
        w.Write(vm.VirtualSize ?? 0); // PeakVirtualSize @72
        w.Write(vm.VirtualSize ?? 0); // VirtualSize @80
        w.Write(vm.PrivateUsage);     // PrivateUsage @88
        w.Seek(136, SeekOrigin.Begin);
        w.Write(vm.JobPrivateCommitLimit ?? 0);
        return bytes;
    }

    private static byte[] _SystemMemory(SystemMemory sm)
    {
        // MINIDUMP_SYSTEM_MEMORY_INFO_1 (492 bytes) with SYSMEMINFO1_BASICPERF
        var bytes = new byte[492];
        var w = new BinaryWriter(new MemoryStream(bytes));
        w.Write((ushort)1);
        w.Write((ushort)0x2);
        w.Seek(8, SeekOrigin.Begin);
        w.Write(sm.PageSize);
        w.Write(sm.PhysicalPages);
        w.Seek(36, SeekOrigin.Begin);
        w.Write(0x7FFF_FFFE_FFFFul);  // MaximumUserModeAddress
        w.Seek(124, SeekOrigin.Begin);
        w.Write(sm.CommittedPages);
        w.Write(sm.CommitLimitPages);
        return bytes;
    }

    private byte[] _Memory64List(Appender appender)
    {
        var data = Memory.SelectMany(b => b.Data).ToArray();
        var baseRva = appender.Append(data);
        var w = _Writer(out var ms);
        w.Write((ulong)Memory.Count);
        w.Write((ulong)baseRva);
        foreach (var block in Memory)
        {
            w.Write(block.Address);
            w.Write((ulong)block.Data.Length);
        }
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

    private byte[] _ModuleList(Appender appender)
    {
        var w = _Writer(out var ms);
        w.Write((uint)Modules.Count);
        foreach (var module in Modules)
        {
            var nameRva = appender.AppendString(module.Path);

            uint cvRva = 0, cvSize = 0;
            if (module.PdbPath is not null)
            {
                var cw = _Writer(out var cvStream);
                cw.Write(0x53445352u); // "RSDS"
                cw.Write((module.PdbGuid ?? Guid.Empty).ToByteArray());
                cw.Write(module.PdbAge);
                cw.Write(Encoding.UTF8.GetBytes(module.PdbPath));
                cw.Write((byte)0);
                var cv = cvStream.ToArray();
                cvSize = (uint)cv.Length;
                cvRva = appender.Append(cv);
            }

            w.Write(module.Base);
            w.Write(module.Size);
            w.Write(0u);                     // checksum
            w.Write(module.TimeDateStamp);
            w.Write(nameRva);
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
    public static byte[] KernelDump64(uint bugCheckCode, params ulong[] parameters) =>
        KernelDump64(bugCheckCode, parameters, dumpType: 5, systemTime: new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero), uptime: TimeSpan.FromHours(50));

    public static byte[] KernelDump64(uint bugCheckCode, ulong[] parameters, uint dumpType, DateTimeOffset systemTime, TimeSpan uptime)
    {
        var bytes = new byte[0x2000];
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
        w.Seek(0xF98, SeekOrigin.Begin);
        w.Write(dumpType);
        w.Seek(0xFA8, SeekOrigin.Begin);
        w.Write(systemTime.UtcDateTime.ToFileTimeUtc());
        w.Seek(0x1030, SeekOrigin.Begin);
        w.Write(uptime.Ticks);
        return bytes;
    }
}
