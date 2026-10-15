using System.Buffers.Binary;
using System.Text;

namespace DumpAnalysisService.Infrastructure.Minidump;

/// <summary>
/// Reads the parts of a Windows dump file that triage needs, by offset: the minidump header and
/// stream directory, then the small metadata streams (system, misc, exception with the faulting
/// thread's registers, threads, modules, unloaded modules, thread info and names, memory regions,
/// handles, VM counters, system memory, comments). Captured memory is never loaded as a whole; a
/// few small reads decode C++ and delay-load exceptions. Kernel dumps only get their header read.
/// Every offset and count is checked against the file length; malformed input raises
/// <see cref="InvalidDataException"/>.
/// </summary>
public static class MinidumpParser
{
    private const uint MinidumpSignature = 0x504D444D; // "MDMP"
    private const uint KernelSignature = 0x45474150;   // "PAGE"
    private const uint KernelValid64 = 0x34365544;     // "DU64"
    private const uint KernelValid32 = 0x504D5544;     // "DUMP"
    private const uint CodeViewRsds = 0x53445352;      // "RSDS"

    private const int MaxStreams = 4096;
    private const int MaxModules = 65536;
    private const int MaxThreads = 1 << 20;
    private const int MaxStringBytes = 64 * 1024;
    private const int MaxListEntries = 1 << 22;
    private const int MaxBlockBytes = 256 * 1024 * 1024;
    private const int MaxCommentBytes = 8 * 1024;

    private const uint ThreadListStream = 3;
    private const uint ModuleListStream = 4;
    private const uint MemoryListStream = 5;
    private const uint ExceptionStream = 6;
    private const uint SystemInfoStream = 7;
    private const uint Memory64ListStream = 9;
    private const uint CommentStreamA = 10;
    private const uint CommentStreamW = 11;
    private const uint HandleDataStream = 12;
    private const uint UnloadedModuleListStream = 14;
    private const uint MiscInfoStream = 15;
    private const uint MemoryInfoListStream = 16;
    private const uint ThreadInfoListStream = 17;
    private const uint SystemMemoryInfoStream = 21;
    private const uint ProcessVmCountersStream = 22;
    private const uint ThreadNamesStream = 24;

    private const int ModuleRecordSize = 108;
    private const int ThreadRecordSize = 48;

    public static MinidumpData Parse(Stream stream)
    {
        if (!stream.CanSeek || !stream.CanRead)
            throw new ArgumentException("Stream must be readable and seekable", nameof(stream));

        var reader = new Reader(stream);
        if (reader.Length < 8)
            throw new InvalidDataException("File is too small to be a dump");

        var signature = reader.UInt32At(0);
        return signature switch
        {
            MinidumpSignature => _ParseMinidump(reader),
            KernelSignature => _ParseKernelHeader(reader),
            _ => throw new InvalidDataException($"Not a Windows dump file (signature 0x{signature:X8})"),
        };
    }

    private static MinidumpData _ParseMinidump(Reader reader)
    {
        reader.Require(0, 32, "minidump header");
        var streamCount = reader.UInt32At(8);
        var directoryRva = reader.UInt32At(12);
        var timestamp = reader.UInt32At(20);
        var flags = reader.UInt64At(24);

        if (streamCount > MaxStreams)
            throw new InvalidDataException($"Implausible stream count {streamCount}");
        reader.Require(directoryRva, streamCount * 12L, "stream directory");

        var streams = new Dictionary<uint, (uint Size, uint Rva)>();
        for (var i = 0; i < streamCount; i++)
        {
            var entry = directoryRva + i * 12L;
            var type = reader.UInt32At(entry);
            var size = reader.UInt32At(entry + 4);
            var rva = reader.UInt32At(entry + 8);
            if (type == 0 || size == 0)
                continue; // unused directory slot
            reader.Require(rva, size, $"stream {type}");
            streams.TryAdd(type, (size, rva));
        }

        (uint Size, uint Rva)? Find(uint type) => streams.TryGetValue(type, out var s) ? s : null;

        var systemInfo = Find(SystemInfoStream) is { } sys ? _ReadSystemInfo(reader, sys.Rva, sys.Size) : null;
        var exception = Find(ExceptionStream) is { } exc ? _ReadException(reader, exc.Rva, exc.Size, systemInfo?.ProcessorArchitecture) : null;
        var memory = MemoryMap.Create(reader, Find(MemoryListStream), Find(Memory64ListStream));
        var pointerSize = systemInfo?.ProcessorArchitecture switch
        {
            Architectures.X86 or Architectures.Arm => 4,
            Architectures.Amd64 or Architectures.Arm64 or Architectures.Ia64 => 8,
            _ => 0,
        };

        return new MinidumpData(
            IsKernelDump: false,
            TimeDateStamp: timestamp == 0 ? null : DateTimeOffset.FromUnixTimeSeconds(timestamp),
            Flags: flags,
            SystemInfo: systemInfo,
            MiscInfo: Find(MiscInfoStream) is { } misc ? _ReadMiscInfo(reader, misc.Rva, misc.Size) : null,
            Exception: exception,
            ThreadIds: Find(ThreadListStream) is { } thr ? _ReadThreadIds(reader, thr.Rva, thr.Size) : [],
            Modules: Find(ModuleListStream) is { } mod ? _ReadModules(reader, mod.Rva, mod.Size) : [],
            Kernel: null)
        {
            StreamTypes = streams.Keys.Order().ToArray(),
            UnloadedModules = Find(UnloadedModuleListStream) is { } unl ? _ReadUnloadedModules(reader, unl.Rva, unl.Size) : [],
            ThreadInfos = Find(ThreadInfoListStream) is { } ti ? _ReadThreadInfos(reader, ti.Rva, ti.Size) : [],
            ThreadNames = Find(ThreadNamesStream) is { } tn ? _ReadThreadNames(reader, tn.Rva, tn.Size) : new Dictionary<uint, string>(),
            MemoryRegions = Find(MemoryInfoListStream) is { } mi ? _ReadMemoryRegions(reader, mi.Rva, mi.Size) : [],
            Handles = Find(HandleDataStream) is { } hd ? _ReadHandles(reader, hd.Rva, hd.Size) : null,
            VmCounters = Find(ProcessVmCountersStream) is { } vm ? _ReadVmCounters(reader, vm.Rva, vm.Size) : null,
            SystemMemory = Find(SystemMemoryInfoStream) is { } sm ? _ReadSystemMemory(reader, sm.Rva, sm.Size) : null,
            Comment = _ReadComment(reader, Find(CommentStreamW), Find(CommentStreamA)),
            CppException = exception?.Code == ExceptionCodes.CppException ? CppExceptionDecoder.Decode(memory, exception) : null,
            DelayLoad = exception?.Code is ExceptionCodes.DelayLoadModuleNotFound or ExceptionCodes.DelayLoadProcNotFound && pointerSize != 0
                ? _ReadDelayLoad(memory, exception, pointerSize)
                : null,
        };
    }

    private static MinidumpSystemInfo _ReadSystemInfo(Reader reader, uint rva, uint size)
    {
        if (size < 32)
            throw new InvalidDataException("System info stream is truncated");

        return new MinidumpSystemInfo(
            ProcessorArchitecture: reader.UInt16At(rva),
            NumberOfProcessors: reader.ByteAt(rva + 6),
            ProductType: reader.ByteAt(rva + 7),
            MajorVersion: reader.UInt32At(rva + 8),
            MinorVersion: reader.UInt32At(rva + 12),
            BuildNumber: reader.UInt32At(rva + 16));
    }

    private static MinidumpMiscInfo? _ReadMiscInfo(Reader reader, uint rva, uint size)
    {
        if (size < 24)
            return null;

        var flags = reader.UInt32At(rva + 4);
        const uint processIdValid = 0x1, processTimesValid = 0x2;
        var timesValid = (flags & processTimesValid) != 0;
        return new MinidumpMiscInfo(
            ProcessId: (flags & processIdValid) != 0 ? reader.UInt32At(rva + 8) : null,
            ProcessCreateTime: timesValid && reader.UInt32At(rva + 12) != 0
                ? DateTimeOffset.FromUnixTimeSeconds(reader.UInt32At(rva + 12))
                : null)
        {
            UserTime = timesValid ? TimeSpan.FromSeconds(reader.UInt32At(rva + 16)) : null,
            KernelTime = timesValid ? TimeSpan.FromSeconds(reader.UInt32At(rva + 20)) : null,
        };
    }

    private static MinidumpException _ReadException(Reader reader, uint rva, uint size, ushort? architecture)
    {
        // MINIDUMP_EXCEPTION_STREAM: ThreadId, alignment, MINIDUMP_EXCEPTION (152 bytes), context location
        if (size < 160)
            throw new InvalidDataException("Exception stream is truncated");

        var record = rva + 8;
        var parameterCount = Math.Min(reader.UInt32At(record + 24), 15u);
        var parameters = new ulong[parameterCount];
        for (var i = 0; i < parameterCount; i++)
            parameters[i] = reader.UInt64At(record + 32 + i * 8L);

        IReadOnlyList<MinidumpRegister> registers = [];
        if (size >= 168)
            registers = _ReadRegisters(reader, reader.UInt32At(rva + 164), reader.UInt32At(rva + 160), architecture);

        return new MinidumpException(
            ThreadId: reader.UInt32At(rva),
            Code: reader.UInt32At(record),
            Flags: reader.UInt32At(record + 4),
            Address: reader.UInt64At(record + 16),
            Parameters: parameters)
        {
            Registers = registers,
        };
    }

    /// <summary>General-purpose registers of the faulting thread from its CONTEXT record (x64 and x86).</summary>
    private static IReadOnlyList<MinidumpRegister> _ReadRegisters(Reader reader, uint rva, uint size, ushort? architecture)
    {
        (string Name, int Offset)[] layout;
        int width;
        switch (architecture)
        {
            case Architectures.Amd64 when size >= 0x100:
                layout = [("rax", 0x78), ("rbx", 0x90), ("rcx", 0x80), ("rdx", 0x88), ("rsi", 0xA8), ("rdi", 0xB0),
                    ("rbp", 0xA0), ("rsp", 0x98), ("r8", 0xB8), ("r9", 0xC0), ("r10", 0xC8), ("r11", 0xD0),
                    ("r12", 0xD8), ("r13", 0xE0), ("r14", 0xE8), ("r15", 0xF0), ("rip", 0xF8)];
                width = 8;
                break;
            case Architectures.X86 when size >= 0xCC:
                layout = [("eax", 0xB0), ("ebx", 0xA4), ("ecx", 0xAC), ("edx", 0xA8), ("esi", 0xA0), ("edi", 0x9C),
                    ("ebp", 0xB4), ("esp", 0xC4), ("eip", 0xB8)];
                width = 4;
                break;
            default:
                return [];
        }

        if (rva == 0)
            return [];
        reader.Require(rva, size, "thread context");
        var block = reader.Bytes(rva, (int)Math.Min(size, 0x200u));
        return layout
            .Select(r => new MinidumpRegister(r.Name, width == 8
                ? BinaryPrimitives.ReadUInt64LittleEndian(block.AsSpan(r.Offset))
                : BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(r.Offset))))
            .ToArray();
    }

    private static IReadOnlyList<uint> _ReadThreadIds(Reader reader, uint rva, uint size)
    {
        var count = reader.UInt32At(rva);
        if (count > MaxThreads || 4L + count * ThreadRecordSize > size)
            throw new InvalidDataException($"Thread list stream is truncated or has an implausible count ({count})");

        var ids = new uint[count];
        for (var i = 0; i < count; i++)
            ids[i] = reader.UInt32At(rva + 4 + i * (long)ThreadRecordSize);
        return ids;
    }

    private static IReadOnlyList<MinidumpModule> _ReadModules(Reader reader, uint rva, uint size)
    {
        var count = reader.UInt32At(rva);
        if (count > MaxModules || 4L + count * ModuleRecordSize > size)
            throw new InvalidDataException($"Module list stream is truncated or has an implausible count ({count})");

        var modules = new List<MinidumpModule>((int)count);
        for (var i = 0; i < count; i++)
        {
            var record = rva + 4 + i * (long)ModuleRecordSize;
            var fileInfo = record + 24; // VS_FIXEDFILEINFO
            var signature = reader.UInt32At(fileInfo);
            string? version = null;
            if (signature == 0xFEEF04BD)
            {
                var ms = reader.UInt32At(fileInfo + 8);
                var ls = reader.UInt32At(fileInfo + 12);
                version = $"{ms >> 16}.{ms & 0xFFFF}.{ls >> 16}.{ls & 0xFFFF}";
            }

            var cvSize = reader.UInt32At(record + 76);
            var cvRva = reader.UInt32At(record + 80);

            modules.Add(new MinidumpModule(
                BaseAddress: reader.UInt64At(record),
                Size: reader.UInt32At(record + 8),
                TimeDateStamp: reader.UInt32At(record + 16),
                Path: reader.MinidumpString(reader.UInt32At(record + 20)),
                FileVersion: version,
                CodeView: cvSize >= 24 ? _ReadCodeView(reader, cvRva, cvSize) : null));
        }
        return modules;
    }

    private static MinidumpCodeView? _ReadCodeView(Reader reader, uint rva, uint size)
    {
        reader.Require(rva, size, "CodeView record");
        if (reader.UInt32At(rva) != CodeViewRsds)
            return null; // older NB10 records carry no GUID

        var guid = new Guid(reader.Bytes(rva + 4, 16));
        var age = reader.UInt32At(rva + 20);
        var nameBytes = reader.Bytes(rva + 24, (int)Math.Min(size - 24, MaxStringBytes));
        var terminator = Array.IndexOf(nameBytes, (byte)0);
        var name = Encoding.UTF8.GetString(nameBytes, 0, terminator < 0 ? nameBytes.Length : terminator);
        return new MinidumpCodeView(name, guid, age);
    }

    /// <summary>
    /// Reads a list stream that starts with a header carrying SizeOfHeader and SizeOfEntry, so entries
    /// written by newer DbgHelp versions (larger than the layout we know) are still stepped correctly.
    /// </summary>
    private static (byte[] Block, int HeaderSize, int EntrySize, int Count) _ReadSizedList(
        Reader reader, uint rva, uint size, int minHeader, int minEntry, long count, string what)
    {
        var block = reader.Block(rva, size, what);
        var headerSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(block);
        var entrySize = (int)BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(4));
        if (headerSize < minHeader || entrySize < minEntry || count < 0 || count > MaxListEntries
            || headerSize + count * entrySize > block.Length)
            throw new InvalidDataException($"The {what} stream is truncated or malformed");
        return (block, headerSize, entrySize, (int)count);
    }

    private static IReadOnlyList<MinidumpUnloadedModule> _ReadUnloadedModules(Reader reader, uint rva, uint size)
    {
        if (size < 12)
            return [];
        var (block, header, stride, count) = _ReadSizedList(reader, rva, size, 12, 24, reader.UInt32At(rva + 8), "unloaded module list");
        var modules = new List<MinidumpUnloadedModule>(count);
        for (var i = 0; i < count; i++)
        {
            var e = block.AsSpan(header + i * stride);
            modules.Add(new MinidumpUnloadedModule(
                BaseAddress: BinaryPrimitives.ReadUInt64LittleEndian(e),
                Size: BinaryPrimitives.ReadUInt32LittleEndian(e[8..]),
                TimeDateStamp: BinaryPrimitives.ReadUInt32LittleEndian(e[16..]),
                Path: reader.MinidumpString(BinaryPrimitives.ReadUInt32LittleEndian(e[20..]))));
        }
        return modules;
    }

    private static IReadOnlyList<MinidumpThreadInfo> _ReadThreadInfos(Reader reader, uint rva, uint size)
    {
        if (size < 12)
            return [];
        var (block, header, stride, count) = _ReadSizedList(reader, rva, size, 12, 64, reader.UInt32At(rva + 8), "thread info list");
        var threads = new List<MinidumpThreadInfo>(count);
        for (var i = 0; i < count; i++)
        {
            var e = block.AsSpan(header + i * stride);
            threads.Add(new MinidumpThreadInfo(
                ThreadId: BinaryPrimitives.ReadUInt32LittleEndian(e),
                DumpFlags: BinaryPrimitives.ReadUInt32LittleEndian(e[4..]),
                ExitStatus: BinaryPrimitives.ReadUInt32LittleEndian(e[12..]),
                CreateTime: _FileTime(BinaryPrimitives.ReadInt64LittleEndian(e[16..])),
                KernelTime: TimeSpan.FromTicks(BinaryPrimitives.ReadInt64LittleEndian(e[32..]) & long.MaxValue),
                UserTime: TimeSpan.FromTicks(BinaryPrimitives.ReadInt64LittleEndian(e[40..]) & long.MaxValue),
                StartAddress: BinaryPrimitives.ReadUInt64LittleEndian(e[48..])));
        }
        return threads;
    }

    private static IReadOnlyDictionary<uint, string> _ReadThreadNames(Reader reader, uint rva, uint size)
    {
        // MINIDUMP_THREAD_NAME_LIST: count, then { ThreadId, RVA64 of a MINIDUMP_STRING } packed to 12 bytes
        var count = reader.UInt32At(rva);
        if (count > MaxThreads || 4L + count * 12L > size)
            throw new InvalidDataException($"Thread names stream is truncated or has an implausible count ({count})");

        var names = new Dictionary<uint, string>();
        for (var i = 0; i < count; i++)
        {
            var entry = rva + 4 + i * 12L;
            var nameRva = reader.UInt64At(entry + 4);
            if (nameRva == 0 || nameRva > (ulong)reader.Length)
                continue;
            var name = reader.MinidumpString((long)nameRva);
            if (name.Length > 0)
                names.TryAdd(reader.UInt32At(entry), name);
        }
        return names;
    }

    private static IReadOnlyList<MinidumpMemoryRegion> _ReadMemoryRegions(Reader reader, uint rva, uint size)
    {
        if (size < 16)
            return [];
        var (block, header, stride, count) = _ReadSizedList(reader, rva, size, 16, 48, (long)Math.Min(reader.UInt64At(rva + 8), long.MaxValue), "memory info list");
        var regions = new MinidumpMemoryRegion[count];
        for (var i = 0; i < count; i++)
        {
            var e = block.AsSpan(header + i * stride);
            regions[i] = new MinidumpMemoryRegion(
                BaseAddress: BinaryPrimitives.ReadUInt64LittleEndian(e),
                AllocationBase: BinaryPrimitives.ReadUInt64LittleEndian(e[8..]),
                AllocationProtect: BinaryPrimitives.ReadUInt32LittleEndian(e[16..]),
                RegionSize: BinaryPrimitives.ReadUInt64LittleEndian(e[24..]),
                State: BinaryPrimitives.ReadUInt32LittleEndian(e[32..]),
                Protect: BinaryPrimitives.ReadUInt32LittleEndian(e[36..]),
                Type: BinaryPrimitives.ReadUInt32LittleEndian(e[40..]));
        }
        Array.Sort(regions, (a, b) => a.BaseAddress.CompareTo(b.BaseAddress));
        return regions;
    }

    private static MinidumpHandleSummary? _ReadHandles(Reader reader, uint rva, uint size)
    {
        // MINIDUMP_HANDLE_DATA_STREAM: SizeOfHeader, SizeOfDescriptor, NumberOfDescriptors, Reserved
        if (size < 16)
            return null;
        var (block, header, stride, count) = _ReadSizedList(reader, rva, size, 16, 32, reader.UInt32At(rva + 8), "handle data");
        var typeNames = new Dictionary<uint, string>();
        var byType = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            var typeRva = BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(header + i * stride + 8));
            if (!typeNames.TryGetValue(typeRva, out var type))
            {
                type = typeRva == 0 ? "(unknown)" : reader.MinidumpString(typeRva);
                if (type.Length == 0)
                    type = "(unknown)";
                typeNames[typeRva] = type;
            }
            byType[type] = byType.GetValueOrDefault(type) + 1;
        }
        return new MinidumpHandleSummary(count, byType);
    }

    private static MinidumpVmCounters? _ReadVmCounters(Reader reader, uint rva, uint size)
    {
        var revision = reader.UInt16At(rva);
        if (revision == 1 && size >= 80)
        {
            // MINIDUMP_PROCESS_VM_COUNTERS_1
            return new MinidumpVmCounters(
                PageFaultCount: reader.UInt32At(rva + 4),
                PeakWorkingSetSize: reader.UInt64At(rva + 8),
                WorkingSetSize: reader.UInt64At(rva + 16),
                PrivateUsage: reader.UInt64At(rva + 72),
                PeakPrivateUsage: reader.UInt64At(rva + 64),
                VirtualSize: null,
                PeakVirtualSize: null,
                JobPrivateCommitLimit: null);
        }

        if (revision >= 2 && size >= 152)
        {
            // MINIDUMP_PROCESS_VM_COUNTERS_2; Flags says which groups are valid
            var flags = reader.UInt16At(rva + 2);
            const ushort virtualSize = 0x2, ex = 0x4, job = 0x10;
            var jobLimit = (flags & job) != 0 ? reader.UInt64At(rva + 136) : 0;
            return new MinidumpVmCounters(
                PageFaultCount: reader.UInt32At(rva + 4),
                PeakWorkingSetSize: reader.UInt64At(rva + 8),
                WorkingSetSize: reader.UInt64At(rva + 16),
                // Without the EX group, PagefileUsage (the commit charge) is the same number.
                PrivateUsage: (flags & ex) != 0 ? reader.UInt64At(rva + 88) : reader.UInt64At(rva + 56),
                PeakPrivateUsage: reader.UInt64At(rva + 64),
                VirtualSize: (flags & virtualSize) != 0 ? reader.UInt64At(rva + 80) : null,
                PeakVirtualSize: (flags & virtualSize) != 0 ? reader.UInt64At(rva + 72) : null,
                JobPrivateCommitLimit: jobLimit == 0 ? null : jobLimit);
        }

        return null;
    }

    private static MinidumpSystemMemory? _ReadSystemMemory(Reader reader, uint rva, uint size)
    {
        // MINIDUMP_SYSTEM_MEMORY_INFO_1: Revision, Flags, BasicInfo @4, FileCacheInfo @56,
        // BasicPerfInfo @116 (64-bit page counts, valid with SYSMEMINFO1_BASICPERF), PerfInfo @148.
        if (size < 204)
            return null;
        var flags = reader.UInt16At(rva + 2);
        const ushort basicPerf = 0x2;
        var pageSize = reader.UInt32At(rva + 8);
        if (pageSize == 0)
            return null;

        ulong committed, limit;
        if ((flags & basicPerf) != 0)
        {
            committed = reader.UInt64At(rva + 124);
            limit = reader.UInt64At(rva + 132);
        }
        else
        {
            committed = reader.UInt32At(rva + 196);
            limit = reader.UInt32At(rva + 200);
        }

        return new MinidumpSystemMemory(
            PageSize: pageSize,
            PhysicalMemoryBytes: (ulong)reader.UInt32At(rva + 12) * pageSize,
            CommittedBytes: committed * pageSize,
            CommitLimitBytes: limit * pageSize,
            MaximumUserModeAddress: reader.UInt64At(rva + 36));
    }

    private static string? _ReadComment(Reader reader, (uint Size, uint Rva)? wide, (uint Size, uint Rva)? ansi)
    {
        string? text = null;
        if (wide is { } w)
            text = Encoding.Unicode.GetString(reader.Bytes(w.Rva, (int)Math.Min(w.Size & ~1u, MaxCommentBytes)));
        else if (ansi is { } a)
            text = Encoding.Latin1.GetString(reader.Bytes(a.Rva, (int)Math.Min(a.Size, MaxCommentBytes)));
        if (text is null)
            return null;
        var terminator = text.IndexOf('\0');
        text = (terminator < 0 ? text : text[..terminator]).Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>
    /// The delay-load helper raises 0xC06D007E/7F with a pointer to its DelayLoadInfo as the only
    /// parameter; the DLL and function names are read from captured memory.
    /// </summary>
    private static MinidumpDelayLoad? _ReadDelayLoad(MemoryMap memory, MinidumpException exception, int pointerSize)
    {
        if (exception.Parameters.Count < 1)
            return null;
        var info = exception.Parameters[0];
        // x64: szDll @24, dlp.fImportByName @32, dlp.szProcName @40, dwLastError @64; x86: @12, @16, @20, @32
        var (dllOffset, byNameOffset, procOffset, lastErrorOffset) = pointerSize == 8 ? (24, 32, 40, 64) : (12, 16, 20, 32);

        var dll = memory.ReadPointer(info + (ulong)dllOffset, pointerSize) is { } dllPtr ? memory.ReadAnsiString(dllPtr, 260) : null;
        if (dll is null)
            return null;

        string? procedure = null;
        if (memory.ReadUInt32(info + (ulong)byNameOffset) is { } byName)
        {
            procedure = byName != 0
                ? (memory.ReadPointer(info + (ulong)procOffset, pointerSize) is { } procPtr ? memory.ReadAnsiString(procPtr, 512) : null)
                : memory.ReadUInt32(info + (ulong)procOffset) is { } ordinal ? $"#{ordinal}" : null;
        }

        return new MinidumpDelayLoad(dll, procedure, memory.ReadUInt32(info + (ulong)lastErrorOffset));
    }

    private static MinidumpData _ParseKernelHeader(Reader reader)
    {
        var valid = reader.UInt32At(4);
        MinidumpKernelHeader header;
        if (valid == KernelValid64)
        {
            reader.Require(0, 96, "kernel dump header");
            // DUMP_HEADER64: DumpType @0xF98, SystemTime @0xFA8, SystemUpTime @0x1030
            var extended = reader.Length >= 0x1038;
            header = new MinidumpKernelHeader(
                Is64Bit: true,
                MachineImageType: reader.UInt32At(48),
                NumberProcessors: reader.UInt32At(52),
                BugCheckCode: reader.UInt32At(56),
                Parameters: [reader.UInt64At(64), reader.UInt64At(72), reader.UInt64At(80), reader.UInt64At(88)])
            {
                DumpType = extended ? reader.UInt32At(0xF98) : null,
                SystemTime = extended ? _FileTime((long)reader.UInt64At(0xFA8)) : null,
                SystemUptime = extended ? _Uptime((long)reader.UInt64At(0x1030)) : null,
            };
        }
        else if (valid == KernelValid32)
        {
            reader.Require(0, 60, "kernel dump header");
            // DUMP_HEADER32: DumpType @0xF88, SystemUpTime @0xFB8, SystemTime @0xFC0
            var extended = reader.Length >= 0xFC8;
            header = new MinidumpKernelHeader(
                Is64Bit: false,
                MachineImageType: reader.UInt32At(32),
                NumberProcessors: reader.UInt32At(36),
                BugCheckCode: reader.UInt32At(40),
                Parameters: [reader.UInt32At(44), reader.UInt32At(48), reader.UInt32At(52), reader.UInt32At(56)])
            {
                DumpType = extended ? reader.UInt32At(0xF88) : null,
                SystemTime = extended ? _FileTime((long)reader.UInt64At(0xFC0)) : null,
                SystemUptime = extended ? _Uptime((long)reader.UInt64At(0xFB8)) : null,
            };
        }
        else
        {
            throw new InvalidDataException($"Unknown kernel dump header (0x{valid:X8})");
        }

        return new MinidumpData(
            IsKernelDump: true,
            TimeDateStamp: null,
            Flags: 0,
            SystemInfo: null,
            MiscInfo: null,
            Exception: null,
            ThreadIds: [],
            Modules: [],
            Kernel: header);
    }

    private static DateTimeOffset? _FileTime(long value) =>
        value > 0 && value < DateTime.MaxValue.ToFileTimeUtc() ? new DateTimeOffset(DateTime.FromFileTimeUtc(value)) : null;

    private static TimeSpan? _Uptime(long ticks) =>
        ticks > 0 && ticks < TimeSpan.FromDays(36500).Ticks ? TimeSpan.FromTicks(ticks) : null;

    /// <summary>Bounds-checked little-endian reads at absolute offsets.</summary>
    internal sealed class Reader(Stream stream)
    {
        private readonly byte[] _buffer = new byte[8];

        public long Length { get; } = stream.Length;

        public void Require(long offset, long size, string what)
        {
            if (offset < 0 || size < 0 || offset + size > Length)
                throw new InvalidDataException($"The {what} lies outside the file (offset {offset}, size {size}, file length {Length})");
        }

        public byte ByteAt(long offset) => _Read(offset, 1)[0];

        public ushort UInt16At(long offset) => BitConverter.ToUInt16(_Read(offset, 2));

        public uint UInt32At(long offset) => BitConverter.ToUInt32(_Read(offset, 4));

        public ulong UInt64At(long offset) => BitConverter.ToUInt64(_Read(offset, 8));

        public byte[] Bytes(long offset, int count)
        {
            Require(offset, count, "data");
            var bytes = new byte[count];
            stream.Position = offset;
            stream.ReadExactly(bytes);
            return bytes;
        }

        /// <summary>A whole stream in one read, for the list streams that are parsed entry by entry.</summary>
        public byte[] Block(long offset, uint size, string what)
        {
            if (size > MaxBlockBytes)
                throw new InvalidDataException($"The {what} stream is implausibly large ({size} bytes)");
            Require(offset, size, what);
            return Bytes(offset, (int)size);
        }

        /// <summary>MINIDUMP_STRING: a 32-bit byte length followed by UTF-16LE text.</summary>
        public string MinidumpString(long rva)
        {
            var length = UInt32At(rva);
            if (length > MaxStringBytes || length % 2 != 0)
                throw new InvalidDataException($"Implausible string length {length} at 0x{rva:X}");
            return Encoding.Unicode.GetString(Bytes(rva + 4, (int)length));
        }

        private ReadOnlySpan<byte> _Read(long offset, int count)
        {
            Require(offset, count, "data");
            stream.Position = offset;
            stream.ReadExactly(_buffer, 0, count);
            return _buffer.AsSpan(0, count);
        }
    }

    /// <summary>
    /// Virtual address to file offset lookup over the captured memory ranges (MemoryListStream and
    /// Memory64ListStream). Reads that are not fully inside one captured range return null.
    /// </summary>
    internal sealed class MemoryMap
    {
        private readonly Reader _reader;
        private readonly (ulong Start, ulong Size, long FileOffset)[] _ranges;

        private MemoryMap(Reader reader, (ulong, ulong, long)[] ranges)
        {
            _reader = reader;
            _ranges = ranges;
            Array.Sort(_ranges, (a, b) => a.Start.CompareTo(b.Start));
        }

        public static MemoryMap Create(Reader reader, (uint Size, uint Rva)? memoryList, (uint Size, uint Rva)? memory64List)
        {
            var ranges = new List<(ulong, ulong, long)>();
            if (memoryList is { } ml && ml.Size >= 4)
            {
                // MINIDUMP_MEMORY_LIST: count, then { StartOfMemoryRange, DataSize, Rva } (16 bytes)
                var count = reader.UInt32At(ml.Rva);
                if (count <= MaxListEntries && 4L + count * 16L <= ml.Size)
                {
                    for (var i = 0; i < count; i++)
                    {
                        var d = ml.Rva + 4 + i * 16L;
                        ranges.Add((reader.UInt64At(d), reader.UInt32At(d + 8), reader.UInt32At(d + 12)));
                    }
                }
            }

            if (memory64List is { } m64 && m64.Size >= 16)
            {
                // MINIDUMP_MEMORY64_LIST: count, BaseRva, then { StartOfMemoryRange, DataSize }; data is contiguous
                var count = reader.UInt64At(m64.Rva);
                var offset = (long)Math.Min(reader.UInt64At(m64.Rva + 8), long.MaxValue);
                if (count <= MaxListEntries && 16 + (long)count * 16 <= m64.Size)
                {
                    for (var i = 0L; i < (long)count && offset < reader.Length; i++)
                    {
                        var d = m64.Rva + 16 + i * 16;
                        var size = reader.UInt64At(d + 8);
                        ranges.Add((reader.UInt64At(d), size, offset));
                        offset += (long)Math.Min(size, (ulong)long.MaxValue / 2);
                    }
                }
            }

            return new MemoryMap(reader, ranges.ToArray());
        }

        public byte[]? Read(ulong address, int count)
        {
            var low = 0;
            var high = _ranges.Length - 1;
            while (low <= high)
            {
                var mid = (low + high) / 2;
                var (start, size, fileOffset) = _ranges[mid];
                if (address < start)
                {
                    high = mid - 1;
                }
                else if (address - start >= size)
                {
                    low = mid + 1;
                }
                else
                {
                    var available = size - (address - start);
                    if ((ulong)count > available)
                        return null;
                    var position = fileOffset + (long)(address - start);
                    return position + count <= _reader.Length ? _reader.Bytes(position, count) : null;
                }
            }
            return null;
        }

        public uint? ReadUInt32(ulong address) =>
            Read(address, 4) is { } b ? BinaryPrimitives.ReadUInt32LittleEndian(b) : null;

        public ulong? ReadPointer(ulong address, int pointerSize) =>
            Read(address, pointerSize) is { } b
                ? pointerSize == 8 ? BinaryPrimitives.ReadUInt64LittleEndian(b) : BinaryPrimitives.ReadUInt32LittleEndian(b)
                : null;

        /// <summary>Reads a NUL-terminated single-byte string, giving up at <paramref name="maxLength"/>.</summary>
        public string? ReadAnsiString(ulong address, int maxLength)
        {
            var bytes = new List<byte>();
            while (bytes.Count < maxLength)
            {
                var chunk = Read(address + (ulong)bytes.Count, 1);
                if (chunk is null)
                    return null;
                if (chunk[0] == 0)
                    return Encoding.Latin1.GetString(bytes.ToArray());
                bytes.Add(chunk[0]);
            }
            return null;
        }
    }
}

/// <summary>PROCESSOR_ARCHITECTURE_* values of MINIDUMP_SYSTEM_INFO.</summary>
public static class Architectures
{
    public const ushort X86 = 0;
    public const ushort Arm = 5;
    public const ushort Ia64 = 6;
    public const ushort Amd64 = 9;
    public const ushort Arm64 = 12;
}

public sealed record MinidumpData(
    bool IsKernelDump,
    DateTimeOffset? TimeDateStamp,
    ulong Flags,
    MinidumpSystemInfo? SystemInfo,
    MinidumpMiscInfo? MiscInfo,
    MinidumpException? Exception,
    IReadOnlyList<uint> ThreadIds,
    IReadOnlyList<MinidumpModule> Modules,
    MinidumpKernelHeader? Kernel)
{
    /// <summary>MiniDumpWithFullMemory</summary>
    public bool HasFullMemory => (Flags & 0x2) != 0;

    /// <summary>Stream types present in the directory, ascending.</summary>
    public IReadOnlyList<uint> StreamTypes { get; init; } = [];

    public IReadOnlyList<MinidumpUnloadedModule> UnloadedModules { get; init; } = [];
    public IReadOnlyList<MinidumpThreadInfo> ThreadInfos { get; init; } = [];
    public IReadOnlyDictionary<uint, string> ThreadNames { get; init; } = new Dictionary<uint, string>();

    /// <summary>Address space regions sorted by base address (MemoryInfoListStream).</summary>
    public IReadOnlyList<MinidumpMemoryRegion> MemoryRegions { get; init; } = [];

    public MinidumpHandleSummary? Handles { get; init; }
    public MinidumpVmCounters? VmCounters { get; init; }
    public MinidumpSystemMemory? SystemMemory { get; init; }
    public string? Comment { get; init; }
    public MinidumpCppException? CppException { get; init; }
    public MinidumpDelayLoad? DelayLoad { get; init; }

    /// <summary>The region containing <paramref name="address"/>, by binary search.</summary>
    public MinidumpMemoryRegion? RegionOf(ulong address)
    {
        var regions = MemoryRegions;
        int low = 0, high = regions.Count - 1;
        while (low <= high)
        {
            var mid = (low + high) / 2;
            var region = regions[mid];
            if (address < region.BaseAddress)
                high = mid - 1;
            else if (address - region.BaseAddress >= region.RegionSize)
                low = mid + 1;
            else
                return region;
        }
        return null;
    }
}

public sealed record MinidumpSystemInfo(
    ushort ProcessorArchitecture,
    byte NumberOfProcessors,
    byte ProductType,
    uint MajorVersion,
    uint MinorVersion,
    uint BuildNumber);

public sealed record MinidumpMiscInfo(uint? ProcessId, DateTimeOffset? ProcessCreateTime)
{
    public TimeSpan? UserTime { get; init; }
    public TimeSpan? KernelTime { get; init; }
}

public sealed record MinidumpException(uint ThreadId, uint Code, uint Flags, ulong Address, IReadOnlyList<ulong> Parameters)
{
    /// <summary>General-purpose registers of the faulting thread; empty when the context is missing or the architecture is not x64/x86.</summary>
    public IReadOnlyList<MinidumpRegister> Registers { get; init; } = [];
}

public sealed record MinidumpRegister(string Name, ulong Value);

public sealed record MinidumpModule(
    ulong BaseAddress,
    uint Size,
    uint TimeDateStamp,
    string Path,
    string? FileVersion,
    MinidumpCodeView? CodeView)
{
    public bool Contains(ulong address) => address >= BaseAddress && address - BaseAddress < Size;
}

public sealed record MinidumpCodeView(string PdbPath, Guid Guid, uint Age);

public sealed record MinidumpUnloadedModule(ulong BaseAddress, uint Size, uint TimeDateStamp, string Path)
{
    public bool Contains(ulong address) => address >= BaseAddress && address - BaseAddress < Size;
}

/// <param name="DumpFlags">MINIDUMP_THREAD_INFO_*: 0x4 = the thread had exited</param>
public sealed record MinidumpThreadInfo(
    uint ThreadId,
    uint DumpFlags,
    uint ExitStatus,
    DateTimeOffset? CreateTime,
    TimeSpan KernelTime,
    TimeSpan UserTime,
    ulong StartAddress)
{
    public bool HasExited => (DumpFlags & 0x4) != 0;
}

/// <param name="State">MEM_COMMIT 0x1000, MEM_RESERVE 0x2000, MEM_FREE 0x10000</param>
/// <param name="Type">MEM_PRIVATE 0x20000, MEM_MAPPED 0x40000, MEM_IMAGE 0x1000000</param>
public sealed record MinidumpMemoryRegion(
    ulong BaseAddress,
    ulong AllocationBase,
    uint AllocationProtect,
    ulong RegionSize,
    uint State,
    uint Protect,
    uint Type);

public sealed record MinidumpHandleSummary(int Count, IReadOnlyDictionary<string, int> ByType);

public sealed record MinidumpVmCounters(
    uint PageFaultCount,
    ulong PeakWorkingSetSize,
    ulong WorkingSetSize,
    ulong PrivateUsage,
    ulong PeakPrivateUsage,
    ulong? VirtualSize,
    ulong? PeakVirtualSize,
    ulong? JobPrivateCommitLimit);

public sealed record MinidumpSystemMemory(
    uint PageSize,
    ulong PhysicalMemoryBytes,
    ulong CommittedBytes,
    ulong CommitLimitBytes,
    ulong MaximumUserModeAddress);

/// <param name="TypeNames">Catchable types, most derived first, e.g. "std::out_of_range", "std::logic_error", "std::exception"</param>
/// <param name="Message">std::exception::what() when the thrown type derives from std::exception</param>
public sealed record MinidumpCppException(IReadOnlyList<string> TypeNames, string? Message);

public sealed record MinidumpDelayLoad(string Dll, string? Procedure, uint? LastError);

public sealed record MinidumpKernelHeader(
    bool Is64Bit,
    uint MachineImageType,
    uint NumberProcessors,
    uint BugCheckCode,
    IReadOnlyList<ulong> Parameters)
{
    public uint? DumpType { get; init; }
    public DateTimeOffset? SystemTime { get; init; }
    public TimeSpan? SystemUptime { get; init; }
}
