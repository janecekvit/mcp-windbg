using System.Text;
using DumpAnalysisService.Infrastructure.Minidump;
using DumpAnalysisService.Security;
using DumpAnalysisService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shared.Configuration;
using Shared.Models;

namespace DumpAnalysisService.Tests.Minidump;

/// <summary>
/// The states triage recognises beyond the basic exception record: decoded exception details,
/// access violation classes, fill patterns, unloaded modules, thread CPU, memory and handle
/// pressure, dump contents, kernel bug check decoding and the symbol cache check.
/// </summary>
public sealed class DumpTriageStateTests : IDisposable
{
    private const ulong AppBase = 0x7ff6_0000_0000;
    private static readonly Guid AppPdbGuid = Guid.Parse("0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0");
    private static readonly Guid NtdllPdbGuid = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "triage-states-" + Guid.NewGuid().ToString("N"));

    public DumpTriageStateTests() => Directory.CreateDirectory(_directory);

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

    private static MinidumpBuilder App(MinidumpBuilder.Exception? fault = null)
    {
        var builder = new MinidumpBuilder { Fault = fault };
        builder.Modules.Add(new(AppBase, 0x20000, @"C:\Apps\Contoso\contoso.exe", FileVersion: "2.5.0.17",
            PdbPath: @"D:\build\obj\contoso.pdb", PdbGuid: AppPdbGuid, PdbAge: 3));
        builder.Modules.Add(new(0x7ffa_0000_0000, 0x1F0000, @"C:\Windows\SYSTEM32\ntdll.dll", FileVersion: "10.0.26100.1",
            PdbPath: "ntdll.pdb", PdbGuid: NtdllPdbGuid, PdbAge: 1));
        return builder;
    }

    private static MinidumpBuilder.Exception Av(ulong address, ulong access, ulong target) =>
        new(200, ExceptionCodes.AccessViolation, address, [access, target]);

    private static DumpTriageResult _Triage(MinidumpBuilder builder) =>
        DumpTriageService.Build("x.dmp", 1, null, MinidumpParser.Parse(new MemoryStream(builder.Build())));

    // ---- parser: the extra streams

    [Fact]
    public void Parse_ReadsRegistersUnloadedModulesThreadsRegionsHandlesCountersAndComment()
    {
        var builder = App(Av(AppBase + 0x1234, 0, 0x10));
        builder.Registers = new() { ["rax"] = 0x1111, ["rcx"] = 0xdddd_dddd_dddd_dddd, ["rip"] = AppBase + 0x1234 };
        builder.UnloadedModules.Add(new(0x7ff8_0000_0000, 0x5000, @"C:\Plugins\old.dll"));
        builder.ThreadInfos.Add(new(200, DateTimeOffset.FromUnixTimeSeconds(1_759_999_990), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1), AppBase + 0x500));
        builder.ThreadNames[200] = "Worker";
        builder.MemoryRegions.Add(new(0, 0x10000, 0x10000));
        builder.HandleTypes = ["Event", "Event", "File"];
        builder.ProcessVmCounters = new(PrivateUsage: 100 << 20, PeakPrivateUsage: 120 << 20, WorkingSet: 50 << 20, PeakWorkingSet: 60 << 20, VirtualSize: 2L << 30);
        builder.SystemMemoryInfo = new(CommittedPages: 1000, CommitLimitPages: 4000, PhysicalPages: 2000);
        builder.Comment = "ProcDump -ma -e";
        builder.ProcessTimes = (7, 2);

        var data = MinidumpParser.Parse(new MemoryStream(builder.Build()));

        Assert.Equal(0xdddd_dddd_dddd_ddddul, Assert.Single(data.Exception!.Registers, r => r.Name == "rcx").Value);
        Assert.Equal(AppBase + 0x1234, Assert.Single(data.Exception.Registers, r => r.Name == "rip").Value);
        Assert.Equal(@"C:\Plugins\old.dll", Assert.Single(data.UnloadedModules).Path);
        var thread = Assert.Single(data.ThreadInfos);
        Assert.Equal(TimeSpan.FromSeconds(3), thread.UserTime);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_759_999_990), thread.CreateTime);
        Assert.Equal("Worker", data.ThreadNames[200]);
        Assert.Equal(0x10000u, Assert.Single(data.MemoryRegions).State);
        Assert.Equal(3, data.Handles!.Count);
        Assert.Equal(2, data.Handles.ByType["Event"]);
        Assert.Equal(100ul << 20, data.VmCounters!.PrivateUsage);
        Assert.Equal(2ul << 30, data.VmCounters.VirtualSize);
        Assert.Equal(4000ul * 4096, data.SystemMemory!.CommitLimitBytes);
        Assert.Equal("ProcDump -ma -e", data.Comment);
        Assert.Equal(TimeSpan.FromSeconds(9), data.MiscInfo!.UserTime + data.MiscInfo.KernelTime);
        Assert.Contains(12u, data.StreamTypes);
    }

    [Fact]
    public void Parse_X86Context_ReadsThirtyTwoBitRegisters()
    {
        var builder = App(Av(0x0040_1234, 0, 0xDDDD_DDDD));
        builder.Architecture = 0;
        builder.Registers = new() { ["eax"] = 0xDDDD_DDDD, ["eip"] = 0x0040_1234 };

        var data = MinidumpParser.Parse(new MemoryStream(builder.Build()));

        Assert.Equal(0xDDDD_DDDDul, Assert.Single(data.Exception!.Registers, r => r.Name == "eax").Value);
        Assert.Equal(0x0040_1234ul, Assert.Single(data.Exception.Registers, r => r.Name == "eip").Value);
    }

    [Fact]
    public void Parse_HandleCountPastStreamEnd_ThrowsInvalidData()
    {
        var builder = App();
        builder.HandleTypes = ["Event"];
        var bytes = builder.Build();
        var data = MinidumpParser.Parse(new MemoryStream(bytes));
        Assert.Equal(1, data.Handles!.Count);

        // Find the handle data stream (type 12) in the directory and inflate its descriptor count.
        var directory = BitConverter.ToUInt32(bytes, 12);
        for (var i = 0; i < BitConverter.ToUInt32(bytes, 8); i++)
        {
            var entry = (int)directory + i * 12;
            if (BitConverter.ToUInt32(bytes, entry) == 12)
                BitConverter.GetBytes(50_000u).CopyTo(bytes, BitConverter.ToUInt32(bytes, entry + 8) + 8);
        }

        Assert.Throws<InvalidDataException>(() => MinidumpParser.Parse(new MemoryStream(bytes)));
    }

    // ---- access violation classes

    [Theory]
    [InlineData(0ul, 0x10ul, "null-pointer")]
    [InlineData(8ul, 0ul, "null-function-pointer")]
    [InlineData(0ul, 0xffff_ffff_ffff_fffful, "non-canonical-address")]
    [InlineData(1ul, 0x0000_9000_0000_0000ul, "non-canonical-address")]
    [InlineData(0ul, 0xdddd_dddd_dddd_dde5ul, "use-after-free-pattern")]
    [InlineData(0ul, 0xfeee_feee_feee_feeeul, "use-after-free-pattern")]
    [InlineData(0ul, 0xcdcd_cdcd_cdcd_cdcdul, "uninitialized-memory-pattern")]
    [InlineData(1ul, 0xbaad_f00d_baad_f00dul, "uninitialized-memory-pattern")]
    [InlineData(0ul, 0xfdfd_fdfd_fdfd_fdfdul, "buffer-overrun-pattern")]
    [InlineData(0ul, 0x1234_5678ul, "wild-pointer")]
    public void Classify_AccessViolationTargets(ulong access, ulong target, string expected)
    {
        var address = access == 8 ? target : AppBase + 0x1234;
        var result = _Triage(App(Av(address, access, target)));

        Assert.Equal(expected, result.Fault!.Classification);
    }

    [Fact]
    public void Classify_ThirtyTwoBitPatternAddress_IsUseAfterFree()
    {
        var builder = App(Av(0x0040_1234, 0, 0xDDDD_DDE1));
        builder.Architecture = 0;

        var result = _Triage(builder);

        Assert.Equal("use-after-free-pattern", result.Fault!.Classification);
        Assert.Contains(result.Fault.SuspiciousValues, v => v.Location == "target" && v.Meaning.Contains("freed"));
        Assert.Contains(result.Hints, h => h.Contains("use after free"));
    }

    [Fact]
    public void NonCanonicalAddress_PointsAtRegistersHoldingFreedMemory()
    {
        var builder = App(Av(AppBase + 0x1234, 0, ulong.MaxValue));
        builder.Registers = new() { ["rax"] = 0x10, ["rcx"] = 0xdddd_dddd_dddd_dddd, ["rsp"] = 0xcccc_cccc_cccc_cccc };

        var result = _Triage(builder);

        Assert.Equal("non-canonical-address", result.Fault!.Classification);
        var suspicious = Assert.Single(result.Fault.SuspiciousValues);
        Assert.Equal("rcx", suspicious.Location);
        Assert.Equal("0xdddddddddddddddd", result.Fault.Registers["rcx"]);
        Assert.Contains(result.Hints, h => h.Contains("non-canonical"));
        Assert.Contains(result.Hints, h => h.Contains("rcx=0xdddddddddddddddd") && h.Contains("freed memory"));
    }

    [Fact]
    public void ExecutionInUnloadedModule_IsCallIntoUnloadedModule()
    {
        var builder = App(Av(0x7ff8_0000_1000, 8, 0x7ff8_0000_1000));
        builder.UnloadedModules.Add(new(0x7ff8_0000_0000, 0x5000, @"C:\Plugins\old-plugin.dll"));

        var result = _Triage(builder);

        Assert.Equal("call-into-unloaded-module", result.Fault!.Classification);
        Assert.Equal("old-plugin.dll", result.Fault.UnloadedModule);
        Assert.Equal("old-plugin.dll", Assert.Single(result.Modules!.Unloaded).Name);
        Assert.Contains(result.Hints, h => h.Contains("unloaded module old-plugin.dll"));
        Assert.DoesNotContain(result.Hints, h => h.Contains("outside every loaded module"));
    }

    [Fact]
    public void ReadInsideUnloadedModule_IsAccessToUnloadedModule()
    {
        var builder = App(Av(AppBase + 0x1234, 0, 0x7ff8_0000_2010));
        builder.UnloadedModules.Add(new(0x7ff8_0000_0000, 0x5000, @"C:\Plugins\old-plugin.dll"));

        Assert.Equal("access-to-unloaded-module", _Triage(builder).Fault!.Classification);
    }

    [Theory]
    [InlineData(0ul, 0x10000u, 0u, "unallocated-address", "free")]
    [InlineData(1ul, 0x2000u, 0u, "reserved-not-committed", "reserve")]
    [InlineData(0ul, 0x1000u, 0x104u, "guard-page", "commit")]
    [InlineData(0ul, 0x1000u, 0x01u, "no-access-page", "commit")]
    [InlineData(8ul, 0x1000u, 0x04u, "execute-non-executable", "commit")]
    [InlineData(1ul, 0x1000u, 0x02u, "write-to-read-only", "commit")]
    [InlineData(0ul, 0x1000u, 0x04u, "wild-pointer", "commit")]
    public void Classify_ByTargetMemoryRegion(ulong access, uint state, uint protect, string expected, string expectedState)
    {
        const ulong target = 0x0000_0200_0000_1000;
        var builder = App(Av(access == 8 ? target : AppBase + 0x1234, access, target));
        builder.MemoryRegions.Add(new(0x0000_0100_0000_0000, 0x1000, 0x10000));
        builder.MemoryRegions.Add(new(0x0000_0200_0000_0000, 0x10000, state, protect, state == 0x10000 ? 0 : 0x20000u));

        var fault = _Triage(builder).Fault!;

        Assert.Equal(expected, fault.Classification);
        Assert.Equal(expectedState, fault.TargetRegion!.State);
    }

    // ---- exception details

    [Theory]
    [InlineData(2ul, "FAST_FAIL_STACK_COOKIE_CHECK_FAILURE", "/GS")]
    [InlineData(7ul, "FAST_FAIL_FATAL_APP_EXIT", "terminated itself on purpose")]
    [InlineData(10ul, "FAST_FAIL_GUARD_ICALL_CHECK_FAILURE", "Control Flow Guard")]
    [InlineData(57ul, "FAST_FAIL_CONTROL_INVALID_RETURN_ADDRESS", "shadow stack")]
    [InlineData(999ul, "0x3e7", "failed check")]
    public void FailFast_DecodesSubcode(ulong subcode, string expectedName, string expectedHint)
    {
        var result = _Triage(App(new(200, ExceptionCodes.FailFast, AppBase + 0x2000, [subcode])));

        Assert.Equal(expectedName, result.Fault!.FastFailCode);
        Assert.Equal("fail-fast", result.Fault.Classification);
        Assert.Contains(result.Hints, h => h.Contains(expectedName) && h.Contains(expectedHint));
    }

    [Theory]
    [InlineData(ExceptionCodes.NoMemory, "out-of-memory", "Out of memory")]
    [InlineData(ExceptionCodes.IllegalInstruction, "illegal-instruction", "AVX")]
    [InlineData(ExceptionCodes.DllNotFound, "dll-load-failure", "dependency")]
    [InlineData(ExceptionCodes.InvalidCRuntimeParameter, "invalid-crt-parameter", "invalid parameter handler")]
    [InlineData(ExceptionCodes.FatalAppExit, "abort", "abort()")]
    [InlineData(ExceptionCodes.PossibleDeadlock, "possible-deadlock", "\"locks\"")]
    [InlineData(ExceptionCodes.StowedException, "stowed-exception", "stowed exception")]
    [InlineData(ExceptionCodes.FatalUserCallbackException, "user-callback-exception", "callback")]
    [InlineData(ExceptionCodes.ControlCExit, "control-c", "not a crash")]
    [InlineData(ExceptionCodes.SetThreadName, "thread-naming", "harmless")]
    [InlineData(ExceptionCodes.IntegerDivideByZero, "integer-arithmetic", "division by zero")]
    [InlineData(ExceptionCodes.BadStack, "exception-chain-corruption", "corrupt")]
    [InlineData(0xE0001234u, "application-exception", "unknown")]
    public void ExceptionCodes_GetClassAndHint(uint code, string expectedClass, string expectedHint)
    {
        var result = _Triage(App(new(200, code, AppBase + 0x2000)));

        Assert.Equal(expectedClass, result.Fault!.Classification);
        Assert.Contains(result.Hints, h => h.Contains(expectedHint, StringComparison.Ordinal));
    }

    [Fact]
    public void InPageError_ReportsUnderlyingStatus()
    {
        var result = _Triage(App(new(200, ExceptionCodes.InPageError, AppBase + 0x2000, [0, 0x1000_0000, 0xC000_00C4])));

        Assert.Equal("in-page-error", result.Fault!.Classification);
        Assert.Contains(result.Hints, h => h.Contains("In-page") && h.Contains("0xC00000C4"));
    }

    [Fact]
    public void CppException64_DecodesThrownTypeAndMessage()
    {
        const ulong throwInfo = AppBase + 0x3000, array = AppBase + 0x3010;
        const ulong typeA = AppBase + 0x3020, typeB = AppBase + 0x3040, typeC = AppBase + 0x3060;
        const ulong descriptorA = AppBase + 0x3100, descriptorB = AppBase + 0x3140, descriptorC = AppBase + 0x3180;
        const ulong exceptionObject = 0x0000_0200_0000_0000, message = exceptionObject + 0x100;

        var image = new byte[0x200];
        void Put32(ulong at, uint value) => BitConverter.GetBytes(value).CopyTo(image, (int)(at - (AppBase + 0x3000)));
        void PutName(ulong descriptor, string name) => Encoding.ASCII.GetBytes(name + "\0").CopyTo(image, (int)(descriptor + 16 - (AppBase + 0x3000)));
        Put32(throwInfo + 12, (uint)(array - AppBase));                 // pCatchableTypeArray (RVA)
        Put32(array, 3);
        Put32(array + 4, (uint)(typeA - AppBase));
        Put32(array + 8, (uint)(typeB - AppBase));
        Put32(array + 12, (uint)(typeC - AppBase));
        Put32(typeA + 4, (uint)(descriptorA - AppBase));                // CatchableType.pType (RVA)
        Put32(typeB + 4, (uint)(descriptorB - AppBase));
        Put32(typeC + 4, (uint)(descriptorC - AppBase));
        PutName(descriptorA, ".?AVout_of_range@std@@");
        PutName(descriptorB, ".?AVlogic_error@std@@");
        PutName(descriptorC, ".?AVexception@std@@");

        var obj = new byte[0x140];
        BitConverter.GetBytes(message).CopyTo(obj, 8);                // std::exception::_Data._What
        Encoding.ASCII.GetBytes("invalid vector subscript\0").CopyTo(obj, 0x100);

        var builder = App(new(200, ExceptionCodes.CppException, 0x7ffa_0000_5000, [0x19930520, exceptionObject, throwInfo, AppBase]));
        builder.Memory.Add(new(AppBase + 0x3000, image));
        builder.Memory.Add(new(exceptionObject, obj));

        var result = _Triage(builder);

        var cpp = result.Fault!.CppException!;
        Assert.Equal("std::out_of_range", cpp.Type);
        Assert.Equal(["std::out_of_range", "std::logic_error", "std::exception"], cpp.CatchableTypes);
        Assert.Equal("invalid vector subscript", cpp.Message);
        Assert.Contains(result.Hints, h => h.Contains("std::out_of_range") && h.Contains("invalid vector subscript"));
    }

    [Fact]
    public void CppException32_DecodesAbsolutePointers()
    {
        const ulong throwInfo = 0x0040_3000, array = 0x0040_3010, type = 0x0040_3020, descriptor = 0x0040_3100;
        var image = new byte[0x200];
        void Put32(ulong at, uint value) => BitConverter.GetBytes(value).CopyTo(image, (int)(at - 0x0040_3000));
        Put32(throwInfo + 12, (uint)array);
        Put32(array, 1);
        Put32(array + 4, (uint)type);
        Put32(type + 4, (uint)descriptor);
        Encoding.ASCII.GetBytes(".?AVParseError@Contoso@@\0").CopyTo(image, (int)(descriptor + 8 - 0x0040_3000));

        var builder = App(new(200, ExceptionCodes.CppException, 0x7700_1000, [0x19930520, 0x0100_0000, throwInfo]));
        builder.Architecture = 0;
        builder.Memory.Add(new(0x0040_3000, image));

        var cpp = _Triage(builder).Fault!.CppException!;

        Assert.Equal("Contoso::ParseError", cpp.Type);
        Assert.Null(cpp.Message);
    }

    [Fact]
    public void CppException_WithoutCapturedMemory_FallsBackToGenericHint()
    {
        var result = _Triage(App(new(200, ExceptionCodes.CppException, 0x7ffa_0000_5000, [0x19930520, 1, AppBase + 0x3000, AppBase])));

        Assert.Null(result.Fault!.CppException);
        Assert.Contains(result.Hints, h => h.Contains("Unhandled C++ exception (throw)"));
    }

    [Theory]
    [InlineData(".?AVout_of_range@std@@", "std::out_of_range")]
    [InlineData(".?AUWidget@ui@contoso@@", "contoso::ui::Widget")]
    [InlineData(".?AVbad_alloc@std@@", "std::bad_alloc")]
    [InlineData(".?AV?$basic_string@DU?$char_traits@D@std@@@std@@", ".?AV?$basic_string@DU?$char_traits@D@std@@@std@@")]
    [InlineData("not-decorated", "not-decorated")]
    public void Undecorate_SimpleNamesOnly(string decorated, string expected)
    {
        Assert.Equal(expected, CppExceptionDecoder.Undecorate(decorated));
    }

    [Fact]
    public void DelayLoad64_NamesDllAndFunction()
    {
        const ulong info = 0x0000_0200_0000_0000;
        var block = new byte[0x200];
        BitConverter.GetBytes(info + 0x100).CopyTo(block, 24);   // szDll
        BitConverter.GetBytes(1u).CopyTo(block, 32);              // dlp.fImportByName
        BitConverter.GetBytes(info + 0x140).CopyTo(block, 40);   // dlp.szProcName
        BitConverter.GetBytes(127u).CopyTo(block, 64);            // dwLastError (ERROR_PROC_NOT_FOUND)
        Encoding.ASCII.GetBytes("contoso-crypto.dll\0").CopyTo(block, 0x100);
        Encoding.ASCII.GetBytes("ContosoSign\0").CopyTo(block, 0x140);

        var builder = App(new(200, ExceptionCodes.DelayLoadProcNotFound, 0x7ffa_1000_2000, [info]));
        builder.Memory.Add(new(info, block));

        var result = _Triage(builder);

        Assert.Equal(new DumpDelayLoadInfo("contoso-crypto.dll", "ContosoSign", 127), result.Fault!.DelayLoad);
        Assert.Equal("delay-load-failure", result.Fault.Classification);
        Assert.Contains(result.Hints, h => h.Contains("ContosoSign") && h.Contains("contoso-crypto.dll") && h.Contains("error 127"));
    }

    // ---- process traits

    [Fact]
    public void Wow64Process_IsThirtyTwoBitWithEffmachHint()
    {
        var builder = App(new(200, ExceptionCodes.Wow64Breakpoint, 0x7700_1000));
        builder.Modules.Add(new(0x7ffa_2000_0000, 0x50000, @"C:\Windows\System32\wow64.dll"));
        builder.Modules.Add(new(0x7ffa_3000_0000, 0x10000, @"C:\Windows\System32\wow64cpu.dll"));

        var result = _Triage(builder);

        Assert.True(result.Process!.Wow64);
        Assert.Equal(32, result.Process.Bitness);
        Assert.Contains(result.Hints, h => h.Contains(".effmach x86"));
    }

    [Fact]
    public void DebugRuntime_IsReported()
    {
        var builder = App(Av(AppBase + 0x1234, 0, 0x10));
        builder.Modules.Add(new(0x7ff9_1000_0000, 0x200000, @"C:\Windows\System32\ucrtbased.dll"));

        var result = _Triage(builder);

        Assert.True(result.Process!.DebugRuntime);
        Assert.Contains(result.Hints, h => h.Contains("debug build"));
    }

    [Fact]
    public void CrashSoonAfterStart_IsStartupFailure()
    {
        var builder = App(Av(AppBase + 0x1234, 0, 0x10));
        builder.ProcessCreateTime = builder.TimeDateStamp - 3;

        Assert.Contains(_Triage(builder).Hints, h => h.Contains("3 s after it started"));
    }

    [Fact]
    public void FaultingThreadName_IsReported()
    {
        var builder = App(Av(AppBase + 0x1234, 0, 0x10));
        builder.ThreadNames[200] = "RenderThread";
        builder.ThreadNames[100] = "Main";

        var result = _Triage(builder);

        Assert.Equal("RenderThread", result.Fault!.ThreadName);
        Assert.Equal([new DumpNamedThread(100, "Main"), new DumpNamedThread(200, "RenderThread")], result.Threads!.Named);
    }

    // ---- threads: busy and idle snapshots

    [Fact]
    public void Snapshot_WithSpinningThread_PointsAtIt()
    {
        var builder = App();
        var dumpTime = DateTimeOffset.FromUnixTimeSeconds(builder.TimeDateStamp);
        builder.ThreadInfos.Add(new(100, dumpTime - TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.1), AppBase + 0x100));
        builder.ThreadInfos.Add(new(200, dumpTime - TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(0.5), AppBase + 0x200));
        builder.ThreadInfos.Add(new(300, dumpTime - TimeSpan.FromSeconds(5), TimeSpan.Zero, TimeSpan.Zero, DumpFlags: 0x4));
        builder.ThreadNames[200] = "Spinner";

        var result = _Triage(builder);

        var busiest = result.Threads!.Busiest[0];
        Assert.Equal(200u, busiest.Id);
        Assert.Equal("Spinner", busiest.Name);
        Assert.Equal(0.95, busiest.CpuShare);
        Assert.Equal("contoso.exe", busiest.StartModule);
        Assert.Equal(1, result.Threads.Exited);
        Assert.Contains(result.Hints, h => h.Contains("Thread 200 (Spinner)") && h.Contains("busy loop"));
    }

    [Fact]
    public void Snapshot_WithIdleThreads_SuggestsWaiting()
    {
        var builder = App();
        var dumpTime = DateTimeOffset.FromUnixTimeSeconds(builder.TimeDateStamp);
        builder.ThreadInfos.Add(new(100, dumpTime - TimeSpan.FromSeconds(600), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
        builder.ThreadInfos.Add(new(200, dumpTime - TimeSpan.FromSeconds(600), TimeSpan.FromSeconds(2), TimeSpan.Zero));

        Assert.Contains(_Triage(builder).Hints, h => h.Contains("No thread was busy"));
    }

    // ---- memory and handles

    [Fact]
    public void ThirtyTwoBitProcess_NearAddressSpaceLimit()
    {
        var builder = App(new(200, ExceptionCodes.NoMemory, 0x7700_1000));
        builder.Architecture = 0;
        builder.MemoryRegions.Add(new(0x0001_0000, 0x7600_0000, 0x1000, 0x04, 0x20000)); // ~1.84 GB committed
        builder.MemoryRegions.Add(new(0x7601_0000, 0x0100_0000, 0x10000));               // 16 MB free
        builder.MemoryRegions.Add(new(0x7701_0000, 0x08FE_0000, 0x2000, 0, 0x20000));     // reserved

        var result = _Triage(builder);

        Assert.Equal(16L << 20, result.Memory!.LargestFreeRegion);
        Assert.Contains(result.Hints, h => h.Contains("32-bit address space is nearly exhausted"));
    }

    [Fact]
    public void Wow64RegionsAboveFourGigabytes_AreIgnoredForTheAddressSpace()
    {
        var builder = App();
        builder.Modules.Add(new(0x7ffa_2000_0000, 0x50000, @"C:\Windows\System32\wow64.dll"));
        builder.MemoryRegions.Add(new(0x0001_0000, 0x1000_0000, 0x1000, 0x04, 0x20000));
        builder.MemoryRegions.Add(new(0x1001_0000, 0x6000_0000, 0x10000));
        builder.MemoryRegions.Add(new(0x7ff0_0000_0000, 0x1_0000_0000, 0x1000, 0x04, 0x20000));

        var memory = _Triage(builder).Memory!;

        Assert.Equal(0x1000_0000L, memory.AddressSpaceUsed);
        Assert.Equal(0x6000_0000L, memory.LargestFreeRegion);
    }

    [Fact]
    public void SystemCommitAndJobLimitAndLargePrivateBytes_AreFlagged()
    {
        var builder = App();
        builder.ProcessVmCounters = new(PrivateUsage: 7L << 30, PeakPrivateUsage: 7L << 30, WorkingSet: 6L << 30, PeakWorkingSet: 6L << 30,
            JobPrivateCommitLimit: 7L << 30);
        builder.SystemMemoryInfo = new(CommittedPages: 3_900_000, CommitLimitPages: 4_000_000, PhysicalPages: 3_000_000);

        var result = _Triage(builder);

        Assert.Equal(7L << 30, result.Memory!.PrivateBytes);
        Assert.Equal(4_000_000L * 4096, result.Memory.SystemCommitLimit);
        Assert.Contains(result.Hints, h => h.Contains("job object commit limit"));
        Assert.Contains(result.Hints, h => h.Contains("System commit charge was 97%"));
        Assert.Contains(result.Hints, h => h.Contains("memory leak is likely"));
    }

    [Fact]
    public void ManyHandles_SuggestLeakWithTopType()
    {
        var builder = App();
        builder.HandleTypes = [.. Enumerable.Repeat("Event", 11_000), .. Enumerable.Repeat("File", 500)];

        var result = _Triage(builder);

        Assert.Equal(11_500, result.Handles!.Count);
        Assert.Equal(new DumpHandleTypeCount("Event", 11_000), result.Handles.ByType[0]);
        Assert.Contains(result.Hints, h => h.Contains("handle leak") && h.Contains("Event"));
    }

    [Fact]
    public void FewHandles_NoLeakHint()
    {
        var builder = App();
        builder.HandleTypes = ["Event", "File"];

        Assert.DoesNotContain(_Triage(builder).Hints, h => h.Contains("handle leak"));
    }

    // ---- dump contents

    [Fact]
    public void Contents_ListStreamsAndComment()
    {
        var builder = App(Av(AppBase + 0x1234, 0, 0x10));
        builder.Comment = "Hung window detected";
        builder.ThreadNames[100] = "Main";

        var result = _Triage(builder);

        Assert.Contains("full-memory", result.Dump.Contents);
        Assert.Contains("thread-names", result.Dump.Contents);
        Assert.Contains("comment", result.Dump.Contents);
        Assert.DoesNotContain("handle-data", result.Dump.Contents);
        Assert.Equal("Hung window detected", result.Dump.Comment);
        Assert.Contains(result.Hints, h => h.Contains("Hung window detected"));
        Assert.Contains(result.Hints, h => h.Contains("No handle data"));
    }

    // ---- kernel dumps

    [Fact]
    public void Kernel_ReadsDumpTypeTimeAndUptime()
    {
        var data = MinidumpParser.Parse(new MemoryStream(MinidumpBuilder.KernelDump64(0xD1, [0x10, 2, 0, 0xfffff800_12345678], 4,
            new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero), TimeSpan.FromHours(30))));

        var result = DumpTriageService.Build("mini.dmp", 8192, null, data);

        var kernel = result.Kernel!;
        Assert.Equal("small", kernel.DumpType);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero), kernel.SystemTimeUtc);
        Assert.Equal(TimeSpan.FromHours(30), kernel.SystemUptime);
        Assert.Equal("A driver touched 0x10 at IRQL 2 (read) from instruction 0xfffff80012345678: usually a driver using an invalid or paged-out pointer at raised IRQL.", kernel.Summary);
        Assert.Contains(result.Hints, h => h.Contains("Small kernel dump"));
        Assert.Contains(result.Hints, h => h.Contains("1.06:00:00"));
    }

    [Theory]
    [InlineData(0x139u, 3ul, "FAST_FAIL_CORRUPT_LIST_ENTRY")]
    [InlineData(0x3Bu, 0xC0000005ul, "EXCEPTION_ACCESS_VIOLATION")]
    [InlineData(0x7Fu, 8ul, "double fault")]
    [InlineData(0x133u, 1ul, "cumulatively")]
    [InlineData(0xEFu, 0xffffa000_00001000ul, "critical process")]
    [InlineData(0x124u, 0ul, "hardware")]
    [InlineData(0xE2u, 0ul, "on purpose")]
    public void Kernel_DescribesCommonBugChecks(uint code, ulong first, string expected)
    {
        var result = DumpTriageService.Build("memory.dmp", 8192, null,
            MinidumpParser.Parse(new MemoryStream(MinidumpBuilder.KernelDump64(code, first, 0, 0, 0))));

        Assert.Contains(expected, result.Kernel!.Summary);
        Assert.Contains(result.Hints, h => h.Contains(expected));
    }

    // ---- symbol cache

    private string _CacheWith(params (string Pdb, Guid Guid, uint Age)[] entries)
    {
        var cache = Path.Combine(_directory, "symcache");
        Directory.CreateDirectory(cache);
        foreach (var (pdb, guid, age) in entries)
        {
            var dir = Path.Combine(cache, pdb, $"{guid:N}".ToUpperInvariant() + $"{age:X}");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, pdb), "pdb");
        }
        return cache;
    }

    private string _WriteDump(MinidumpBuilder builder)
    {
        var path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".dmp");
        File.WriteAllBytes(path, builder.Build());
        return path;
    }

    private static DumpTriageService _Service(SecurityOptions? options = null, DebuggerConfiguration? config = null) =>
        new(NullLogger<DumpTriageService>.Instance, Options.Create(options ?? new SecurityOptions()), config);

    [Fact]
    public async Task SymbolCache_ReportsCachedAndMissingPdbs()
    {
        var cache = _CacheWith(("ntdll.pdb", NtdllPdbGuid, 1));
        var path = _WriteDump(App(Av(AppBase + 0x1234, 0, 0x10)));

        var result = await _Service().TriageAsync(path, cache);

        var symbols = result.Symbols!;
        Assert.Equal(cache, symbols.CachePath);
        Assert.Equal(2, symbols.Checked);
        Assert.Equal(1, symbols.Cached);
        Assert.Equal(["contoso.exe"], symbols.Missing);
        Assert.Equal(0, symbols.WindowsMissing);
        Assert.False(Assert.Single(result.Modules!.Notable, m => m.Name == "contoso.exe").SymbolsCached);
        Assert.Contains(result.Hints, h => h.Contains("1 of 2 PDBs are in the symbol cache"));
        Assert.Contains(result.Hints, h => h.Contains("Not cached: contoso.exe") && h.Contains("symbol_path_extra"));
    }

    [Fact]
    public async Task SymbolCache_AllCached_SaysNoDownload()
    {
        var cache = _CacheWith(("ntdll.pdb", NtdllPdbGuid, 1), ("contoso.pdb", AppPdbGuid, 3));

        var result = await _Service().TriageAsync(_WriteDump(App(Av(AppBase + 0x1234, 0, 0x10))), cache);

        Assert.Equal(2, result.Symbols!.Cached);
        Assert.True(Assert.Single(result.Modules!.Notable, m => m.Name == "contoso.exe").SymbolsCached);
        Assert.Contains(result.Hints, h => h.Contains("All 2 PDBs are already in the symbol cache"));
    }

    [Fact]
    public async Task SymbolCache_TwoTierLayoutAndWrongAgeDoNotMatchFlatOnes()
    {
        var cache = Path.Combine(_directory, "twotier");
        var dir = Path.Combine(cache, "co", "contoso.pdb", $"{AppPdbGuid:N}".ToUpperInvariant() + "3");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(cache, "index2.txt"), "");
        File.WriteAllText(Path.Combine(dir, "contoso.pdb"), "pdb");
        // Age 2 instead of 3: a different build, must not count.
        var stale = Path.Combine(cache, "nt", "ntdll.pdb", $"{NtdllPdbGuid:N}".ToUpperInvariant() + "2");
        Directory.CreateDirectory(stale);
        File.WriteAllText(Path.Combine(stale, "ntdll.pdb"), "pdb");

        var result = await _Service().TriageAsync(_WriteDump(App()), cache);

        Assert.Equal(1, result.Symbols!.Cached);
        Assert.Equal(1, result.Symbols.WindowsMissing);
    }

    [Fact]
    public async Task SymbolCache_DefaultsToServerCache_AndClientOverrideCanBeDisabled()
    {
        var serverCache = _CacheWith(("contoso.pdb", AppPdbGuid, 3));
        var config = new DebuggerConfiguration { DefaultSymbolCache = serverCache };
        var options = new SecurityOptions { AllowClientSymbolOverrides = false };

        var result = await _Service(options, config).TriageAsync(_WriteDump(App()), Path.Combine(_directory, "client-cache"));

        Assert.Equal(serverCache, result.Symbols!.CachePath);
        Assert.Equal(1, result.Symbols.Cached);
    }

    [Fact]
    public async Task SymbolCache_InvalidClientValue_IsRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _Service().TriageAsync(_WriteDump(App()), @"C:\symbols;srv*\\evil\share"));
    }

    [Fact]
    public async Task SymbolCache_NoneConfigured_SkipsTheCheck()
    {
        var result = await _Service().TriageAsync(_WriteDump(App()));

        Assert.Null(result.Symbols);
        Assert.All(result.Modules!.Notable, m => Assert.Null(m.SymbolsCached));
    }
}
