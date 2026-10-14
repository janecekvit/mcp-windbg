using System.Text;

namespace DumpAnalysisService.Infrastructure.Minidump;

/// <summary>
/// Reads the parts of a Windows dump file that triage needs, by offset, without loading
/// memory ranges: the minidump header and stream directory, then the system info, misc info,
/// exception, thread list and module list streams. Kernel dumps only get their header read.
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

    private const uint ThreadListStream = 3;
    private const uint ModuleListStream = 4;
    private const uint ExceptionStream = 6;
    private const uint SystemInfoStream = 7;
    private const uint MiscInfoStream = 15;

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

        return new MinidumpData(
            IsKernelDump: false,
            TimeDateStamp: timestamp == 0 ? null : DateTimeOffset.FromUnixTimeSeconds(timestamp),
            Flags: flags,
            SystemInfo: streams.TryGetValue(SystemInfoStream, out var sys) ? _ReadSystemInfo(reader, sys.Rva, sys.Size) : null,
            MiscInfo: streams.TryGetValue(MiscInfoStream, out var misc) ? _ReadMiscInfo(reader, misc.Rva, misc.Size) : null,
            Exception: streams.TryGetValue(ExceptionStream, out var exc) ? _ReadException(reader, exc.Rva, exc.Size) : null,
            ThreadIds: streams.TryGetValue(ThreadListStream, out var thr) ? _ReadThreadIds(reader, thr.Rva, thr.Size) : [],
            Modules: streams.TryGetValue(ModuleListStream, out var mod) ? _ReadModules(reader, mod.Rva, mod.Size) : [],
            Kernel: null);
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
        return new MinidumpMiscInfo(
            ProcessId: (flags & processIdValid) != 0 ? reader.UInt32At(rva + 8) : null,
            ProcessCreateTime: (flags & processTimesValid) != 0 && reader.UInt32At(rva + 12) != 0
                ? DateTimeOffset.FromUnixTimeSeconds(reader.UInt32At(rva + 12))
                : null);
    }

    private static MinidumpException _ReadException(Reader reader, uint rva, uint size)
    {
        // MINIDUMP_EXCEPTION_STREAM: ThreadId, alignment, MINIDUMP_EXCEPTION (152 bytes), context location
        if (size < 160)
            throw new InvalidDataException("Exception stream is truncated");

        var record = rva + 8;
        var parameterCount = Math.Min(reader.UInt32At(record + 24), 15u);
        var parameters = new ulong[parameterCount];
        for (var i = 0; i < parameterCount; i++)
            parameters[i] = reader.UInt64At(record + 32 + i * 8L);

        return new MinidumpException(
            ThreadId: reader.UInt32At(rva),
            Code: reader.UInt32At(record),
            Flags: reader.UInt32At(record + 4),
            Address: reader.UInt64At(record + 16),
            Parameters: parameters);
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

    private static MinidumpData _ParseKernelHeader(Reader reader)
    {
        var valid = reader.UInt32At(4);
        MinidumpKernelHeader header;
        if (valid == KernelValid64)
        {
            reader.Require(0, 96, "kernel dump header");
            header = new MinidumpKernelHeader(
                Is64Bit: true,
                MachineImageType: reader.UInt32At(48),
                NumberProcessors: reader.UInt32At(52),
                BugCheckCode: reader.UInt32At(56),
                Parameters: [reader.UInt64At(64), reader.UInt64At(72), reader.UInt64At(80), reader.UInt64At(88)]);
        }
        else if (valid == KernelValid32)
        {
            reader.Require(0, 60, "kernel dump header");
            header = new MinidumpKernelHeader(
                Is64Bit: false,
                MachineImageType: reader.UInt32At(32),
                NumberProcessors: reader.UInt32At(36),
                BugCheckCode: reader.UInt32At(40),
                Parameters: [reader.UInt32At(44), reader.UInt32At(48), reader.UInt32At(52), reader.UInt32At(56)]);
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

    /// <summary>Bounds-checked little-endian reads at absolute offsets.</summary>
    private sealed class Reader(Stream stream)
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

        /// <summary>MINIDUMP_STRING: a 32-bit byte length followed by UTF-16LE text.</summary>
        public string MinidumpString(uint rva)
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
}

public sealed record MinidumpSystemInfo(
    ushort ProcessorArchitecture,
    byte NumberOfProcessors,
    byte ProductType,
    uint MajorVersion,
    uint MinorVersion,
    uint BuildNumber);

public sealed record MinidumpMiscInfo(uint? ProcessId, DateTimeOffset? ProcessCreateTime);

public sealed record MinidumpException(uint ThreadId, uint Code, uint Flags, ulong Address, IReadOnlyList<ulong> Parameters);

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

public sealed record MinidumpKernelHeader(
    bool Is64Bit,
    uint MachineImageType,
    uint NumberProcessors,
    uint BugCheckCode,
    IReadOnlyList<ulong> Parameters);
