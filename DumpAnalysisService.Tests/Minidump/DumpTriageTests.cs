using DumpAnalysisService.Infrastructure.Minidump;
using DumpAnalysisService.Security;
using DumpAnalysisService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DumpAnalysisService.Tests.Minidump;

public sealed class DumpTriageTests : IDisposable
{
    private static readonly Guid AppPdbGuid = Guid.Parse("0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0");

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "triage-" + Guid.NewGuid().ToString("N"));

    public DumpTriageTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // best-effort cleanup
        }
    }

    private static MinidumpBuilder CrashingApp()
    {
        var builder = new MinidumpBuilder
        {
            Fault = new MinidumpBuilder.Exception(200, ExceptionCodes.AccessViolation, 0x7ff6_0000_1234, [0, 0x10]),
        };
        builder.Modules.Add(new(0x7ff6_0000_0000, 0x20000, @"C:\Apps\Contoso\contoso.exe", FileVersion: "2.5.0.17",
            PdbPath: @"D:\build\obj\contoso.pdb", PdbGuid: AppPdbGuid, PdbAge: 3));
        builder.Modules.Add(new(0x7ffa_0000_0000, 0x1F0000, @"C:\Windows\SYSTEM32\ntdll.dll", FileVersion: "10.0.26100.1"));
        builder.Modules.Add(new(0x7ffa_1000_0000, 0x90000, @"C:\Windows\System32\KERNELBASE.dll"));
        builder.Modules.Add(new(0x7ff9_0000_0000, 0x500000, @"C:\Program Files\dotnet\shared\Microsoft.NETCore.App\10.0.13\coreclr.dll", FileVersion: "10.0.1325.60609"));
        return builder;
    }

    private string _Write(byte[] bytes, string name = "test.dmp")
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static DumpTriageService _Service(SecurityOptions? options = null) =>
        new(NullLogger<DumpTriageService>.Instance, Options.Create(options ?? new SecurityOptions()));

    // ---- parser

    [Fact]
    public void Parse_ReadsHeaderSystemProcessExceptionThreadsAndModules()
    {
        var data = MinidumpParser.Parse(new MemoryStream(CrashingApp().Build()));

        Assert.False(data.IsKernelDump);
        Assert.True(data.HasFullMemory);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_760_000_000), data.TimeDateStamp);
        Assert.Equal((ushort)9, data.SystemInfo!.ProcessorArchitecture);
        Assert.Equal(26100u, data.SystemInfo.BuildNumber);
        Assert.Equal(4242u, data.MiscInfo!.ProcessId);
        Assert.Equal(ExceptionCodes.AccessViolation, data.Exception!.Code);
        Assert.Equal(200u, data.Exception.ThreadId);
        Assert.Equal([100u, 200u, 300u], data.ThreadIds);
        Assert.Equal(4, data.Modules.Count);
        Assert.Equal(@"C:\Apps\Contoso\contoso.exe", data.Modules[0].Path);
        Assert.Equal("2.5.0.17", data.Modules[0].FileVersion);
        Assert.Equal(AppPdbGuid, data.Modules[0].CodeView!.Guid);
        Assert.Equal(3u, data.Modules[0].CodeView!.Age);
        Assert.Equal(@"D:\build\obj\contoso.pdb", data.Modules[0].CodeView!.PdbPath);
    }

    [Fact]
    public void Parse_KernelDumpHeader()
    {
        var data = MinidumpParser.Parse(new MemoryStream(MinidumpBuilder.KernelDump64(0xD1, 0x10, 2, 0, 0xfffff800_12345678)));

        Assert.True(data.IsKernelDump);
        Assert.Equal(0xD1u, data.Kernel!.BugCheckCode);
        Assert.Equal(16u, data.Kernel.NumberProcessors);
        Assert.Equal(0xfffff800_12345678ul, data.Kernel.Parameters[3]);
    }

    [Fact]
    public void Parse_WrongSignature_Throws()
    {
        var ex = Assert.Throws<InvalidDataException>(() => MinidumpParser.Parse(new MemoryStream("not a dump file"u8.ToArray())));
        Assert.Contains("Not a Windows dump", ex.Message);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(31)]
    [InlineData(60)]
    [InlineData(400)]
    public void Parse_TruncatedFile_ThrowsInvalidData(int length)
    {
        var bytes = CrashingApp().Build()[..length];
        Assert.Throws<InvalidDataException>(() => MinidumpParser.Parse(new MemoryStream(bytes)));
    }

    [Fact]
    public void Parse_DirectoryPointingPastEnd_ThrowsInvalidData()
    {
        var bytes = CrashingApp().Build();
        BitConverter.GetBytes(0x7FFF_0000u).CopyTo(bytes, 12); // stream directory RVA
        var ex = Assert.Throws<InvalidDataException>(() => MinidumpParser.Parse(new MemoryStream(bytes)));
        Assert.Contains("outside the file", ex.Message);
    }

    [Fact]
    public void Parse_ImplausibleModuleCount_ThrowsInvalidData()
    {
        var builder = CrashingApp();
        var bytes = builder.Build();
        var data = MinidumpParser.Parse(new MemoryStream(bytes));
        Assert.Equal(4, data.Modules.Count);

        // Corrupt the module count (first dword of the module list stream, the last directory entry).
        var directoryRva = BitConverter.ToUInt32(bytes, 12);
        var streamCount = BitConverter.ToUInt32(bytes, 8);
        var moduleListRva = BitConverter.ToUInt32(bytes, (int)(directoryRva + (streamCount - 1) * 12 + 8));
        BitConverter.GetBytes(1_000_000u).CopyTo(bytes, moduleListRva);

        Assert.Throws<InvalidDataException>(() => MinidumpParser.Parse(new MemoryStream(bytes)));
    }

    // ---- mapping and hints

    [Fact]
    public void Build_AccessViolation_NamesFaultingModuleOffsetAndNullPointerHint()
    {
        var data = MinidumpParser.Parse(new MemoryStream(CrashingApp().Build()));

        var result = DumpTriageService.Build("x.dmp", 1234, null, data);

        Assert.Equal("user-minidump", result.Dump.Format);
        Assert.Equal("x64", result.System!.Architecture);
        Assert.Equal("10.0.26100", result.System.OsVersion);
        Assert.Equal("workstation", result.System.ProductType);
        Assert.Equal("contoso.exe", result.Process!.ImageName);
        Assert.Equal(64, result.Process.Bitness);
        Assert.Equal(4242u, result.Process.ProcessId);
        Assert.Equal(TimeSpan.FromSeconds(1000), result.Process.Uptime);

        var fault = result.Fault!;
        Assert.Equal("0xC0000005", fault.Code);
        Assert.Equal("EXCEPTION_ACCESS_VIOLATION", fault.CodeName);
        Assert.Equal("contoso.exe", fault.Module);
        Assert.Equal("0x1234", fault.ModuleOffset);
        Assert.Equal("read", fault.AccessType);
        Assert.Equal("0x10", fault.TargetAddress);

        Assert.Equal(3, result.Threads!.Count);
        Assert.Equal(200u, result.Threads.FaultingThreadId);

        Assert.Equal(4, result.Modules!.Count);
        Assert.Equal(2, result.Modules.WindowsModuleCount);
        var app = Assert.Single(result.Modules.Notable, m => m.Name == "contoso.exe");
        Assert.True(app.IsFaulting);
        Assert.Equal("contoso.pdb", app.PdbName);
        Assert.Equal(3, app.PdbAge);
        Assert.Equal("0F1E2D3C4B5A69788796A5B4C3D2E1F03", app.SymbolKey);
        Assert.DoesNotContain(result.Modules.Notable, m => m.Name == "ntdll.dll");

        Assert.Equal(".NET", result.Runtime!.Kind);
        Assert.Equal("10.0.1325.60609", result.Runtime.Version);

        Assert.Contains(result.Hints, h => h.Contains("Null-pointer read") && h.Contains("contoso.exe+0x1234"));
        Assert.Contains(result.Hints, h => h.Contains("contoso.pdb") && h.Contains(app.SymbolKey!));
    }

    [Fact]
    public void Build_NoException_IsSnapshotWithThreadsHint()
    {
        var builder = CrashingApp();
        builder.Fault = null;

        var result = DumpTriageService.Build("x.dmp", 1, null, MinidumpParser.Parse(new MemoryStream(builder.Build())));

        Assert.Null(result.Fault);
        Assert.Null(result.Threads!.FaultingThreadId);
        Assert.Contains(result.Hints, h => h.Contains("snapshot"));
    }

    [Fact]
    public void Build_FaultOutsideModules_SaysSo()
    {
        var builder = CrashingApp();
        builder.Fault = new MinidumpBuilder.Exception(100, ExceptionCodes.AccessViolation, 0x1234_5678, [8, 0x1234_5678]);

        var result = DumpTriageService.Build("x.dmp", 1, null, MinidumpParser.Parse(new MemoryStream(builder.Build())));

        Assert.Null(result.Fault!.Module);
        Assert.Equal("execute", result.Fault.AccessType);
        Assert.Contains(result.Hints, h => h.Contains("outside every loaded module"));
    }

    [Theory]
    [InlineData(ExceptionCodes.StackOverflow, "recursion")]
    [InlineData(ExceptionCodes.ClrException, "SOS")]
    [InlineData(ExceptionCodes.FailFast, "Fail-fast")]
    [InlineData(ExceptionCodes.Breakpoint, "snapshot")]
    [InlineData(0xE0001234u, "unknown")]
    public void Build_ExceptionCodes_GetSpecificHints(uint code, string expected)
    {
        var builder = CrashingApp();
        builder.Fault = new MinidumpBuilder.Exception(200, code, 0x7ff6_0000_2000);

        var result = DumpTriageService.Build("x.dmp", 1, null, MinidumpParser.Parse(new MemoryStream(builder.Build())));

        Assert.Contains(result.Hints, h => h.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void Build_KernelDump_ReportsBugCheck()
    {
        var data = MinidumpParser.Parse(new MemoryStream(MinidumpBuilder.KernelDump64(0x133, 1, 0x1E00, 0, 0)));

        var result = DumpTriageService.Build("memory.dmp", 4096, null, data);

        Assert.Equal("kernel-dump", result.Dump.Format);
        Assert.Equal("0x00000133", result.Kernel!.BugCheckCode);
        Assert.Equal("DPC_WATCHDOG_VIOLATION", result.Kernel.BugCheckName);
        Assert.Equal("x64", result.Kernel.Architecture);
        Assert.Contains(result.Hints, h => h.Contains("kernel debugger"));
    }

    [Fact]
    public void Build_MiniDumpWithoutFullMemory_SaysHeapIsLimited()
    {
        var builder = CrashingApp();
        builder.Flags = 0;

        var result = DumpTriageService.Build("x.dmp", 1, null, MinidumpParser.Parse(new MemoryStream(builder.Build())));

        Assert.False(result.Dump.FullMemory);
        Assert.Contains(result.Hints, h => h.Contains("without full memory"));
    }

    // ---- service (files, hashing, security)

    [Fact]
    public async Task TriageAsync_File_HashesAndParses()
    {
        var bytes = CrashingApp().Build();
        var path = _Write(bytes);

        var result = await _Service().TriageAsync(path);

        Assert.Equal(bytes.Length, result.Dump.SizeBytes);
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)), result.Dump.Sha256);
        Assert.Equal("0xC0000005", result.Fault!.Code);
    }

    [Fact]
    public async Task TriageAsync_MissingFile_ThrowsFileNotFound()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() => _Service().TriageAsync(Path.Combine(_directory, "missing.dmp")));
    }

    [Fact]
    public async Task TriageAsync_NotADump_ThrowsInvalidData()
    {
        var path = _Write("hello"u8.ToArray(), "text.dmp");
        await Assert.ThrowsAsync<InvalidDataException>(() => _Service().TriageAsync(path));
    }

    [Fact]
    public async Task TriageAsync_OutsideAllowedDirectories_Throws()
    {
        var path = _Write(CrashingApp().Build());
        var options = new SecurityOptions { AllowedDumpDirectories = [Path.Combine(_directory, "elsewhere")] };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _Service(options).TriageAsync(path));
    }
}
