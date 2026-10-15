using DumpAnalysisService.Fingerprinting;

namespace DumpAnalysisService.Tests.Fingerprinting;

public sealed class CrashFingerprinterTests
{
    private static readonly CrashFingerprinter Fingerprinter = new(FingerprintRules.Load());

    /// <summary>.ecxr; kn of the access-violation scenario (x64, private symbols).</summary>
    private const string AccessViolationStack = """
        rax=0000000000000000 rbx=000000000000000a rcx=0000000000000000
        rip=00007ff61a2b3c44 rsp=000000c377cff6b8 rbp=0000000000000000
        iopl=0         nv up ei pl zr na po nc
        cs=0033  ss=002b  ds=002b  es=002b  fs=0053  gs=002b             efl=00010246
        CrashScenarios!DumpAnalysisService_CrashScenarios_Scenarios__ReadThroughPointer+0x4:
        00007ff6`1a2b3c44 8b00            mov     eax,dword ptr [rax] ds:00000000`00000000=????????
         # Child-SP          RetAddr               Call Site
        00 000000c3`77cff6b8 00007ff6`1a2b3c61     CrashScenarios!DumpAnalysisService_CrashScenarios_Scenarios__ReadThroughPointer+0x4 [D:\a\src\Scenarios.cs @ 50]
        01 000000c3`77cff6c0 00007ff6`1a2b3c8a     CrashScenarios!DumpAnalysisService_CrashScenarios_Scenarios__ValidateRecord+0x11
        02 000000c3`77cff6f0 00007ff6`1a2b3cb3     CrashScenarios!DumpAnalysisService_CrashScenarios_Scenarios__PrepareRecord+0xa
        03 000000c3`77cff720 00007ff6`1a2b3dd1     CrashScenarios!DumpAnalysisService_CrashScenarios_Scenarios__AccessViolationEntry+0x13
        04 000000c3`77cff750 00007ff6`1a2c0012     CrashScenarios!DumpAnalysisService_CrashScenarios_Program___Main_g____|0_0+0x51
        05 000000c3`77cff7a0 00007ffa`10f2e8d7     CrashScenarios!wmain+0x2e
        06 000000c3`77cff7d0 00007ffa`124dc34c     KERNEL32!BaseThreadInitThunk+0x17
        07 000000c3`77cff800 00000000`00000000     ntdll!RtlUserThreadStart+0x2c
        """;

    /// <summary>The same crash in another process (different ASLR bases) of a rebuilt binary (different offsets).</summary>
    private const string AccessViolationStackRebuilt = """
         # Child-SP          RetAddr               Call Site
        00 00000021`a1effa08 00007ff7`88124e71     CrashScenarios!DumpAnalysisService_CrashScenarios_Scenarios__ReadThroughPointer+0x7
        01 00000021`a1effa10 00007ff7`88124e9a     CrashScenarios!DumpAnalysisService_CrashScenarios_Scenarios__ValidateRecord+0x15
        02 00000021`a1effa40 00007ff7`88124ec3     CrashScenarios!DumpAnalysisService_CrashScenarios_Scenarios__PrepareRecord+0xe
        03 00000021`a1effa70 00007ff7`88124fe1     CrashScenarios!DumpAnalysisService_CrashScenarios_Scenarios__AccessViolationEntry+0x17
        04 00000021`a1effaa0 00007ff7`88130022     CrashScenarios!DumpAnalysisService_CrashScenarios_Program___Main_g____|0_0+0x55
        05 00000021`a1effaf0 00007ffb`20f2e8d7     CrashScenarios!wmain+0x32
        06 00000021`a1effb20 00007ffb`224dc34c     KERNEL32!BaseThreadInitThunk+0x17
        07 00000021`a1effb50 00000000`00000000     ntdll!RtlUserThreadStart+0x2c
        """;

    private const string AlternatePathStack = """
         # Child-SP          RetAddr               Call Site
        00 000000c3`77cff6b8 00007ff6`1a2b3d01     CrashScenarios!DumpAnalysisService_CrashScenarios_Scenarios__ReadThroughPointer+0x4
        01 000000c3`77cff6c0 00007ff6`1a2b3d2a     CrashScenarios!DumpAnalysisService_CrashScenarios_Scenarios__LoadFromCache+0x11
        02 000000c3`77cff6f0 00007ff6`1a2b3dd1     CrashScenarios!DumpAnalysisService_CrashScenarios_Scenarios__AlternatePathEntry+0xa
        03 000000c3`77cff750 00007ff6`1a2c0012     CrashScenarios!DumpAnalysisService_CrashScenarios_Program___Main_g____|0_0+0x51
        04 000000c3`77cff7a0 00007ffa`10f2e8d7     CrashScenarios!wmain+0x2e
        """;

    /// <summary>A C++ throw: RaiseException, _CxxThrowException and the msvcp throw helper are shared by every throw.</summary>
    private const string CppThrowStack = """
         # Child-SP          RetAddr               Call Site
        00 000000c3`77cfe9a0 00007ffa`0e1c5f3a     KERNELBASE!RaiseException+0x6c
        01 000000c3`77cfea80 00007ffa`0b2a1234     VCRUNTIME140!_CxxThrowException+0x9a [D:\a\_work\1\s\src\vctools\crt\vcruntime\src\eh\throw.cpp @ 82]
        02 000000c3`77cfeae0 00007ff6`1a2b4000     MSVCP140!std::_Xout_of_range+0x19
        03 000000c3`77cfeb10 00007ff6`1a2b4021     CrashScenarios!DumpAnalysisService_CrashScenarios_Scenarios__ThrowFromNativeLibrary+0x30
        04 000000c3`77cfeb40 00007ff6`1a2b4050     CrashScenarios!DumpAnalysisService_CrashScenarios_Scenarios__CppExceptionEntry+0x9
        05 000000c3`77cfeb70 00007ff6`1a2c0012     CrashScenarios!wmain+0x2e
        """;

    private static string _Recursion(int depth)
    {
        var lines = new List<string> { " # Child-SP          RetAddr               Call Site" };
        for (var i = 0; i < depth; i++)
            lines.Add($"{i:x2} 000000c3`77c0{i:x4} 00007ff6`1a2b5011     CrashScenarios!DumpAnalysisService_CrashScenarios_Scenarios__RecurseWithoutLimit+0x41");
        return string.Join("\n", lines);
    }

    // ---- normalisation

    [Theory]
    [InlineData("CrashScenarios!Foo+0x1f", "crashscenarios!Foo", FrameKind.Symbol)]
    [InlineData("contoso!std::vector<int,std::allocator<int> >::operator[]+0x12", "contoso!std::vector::operator[]", FrameKind.Symbol)]
    [InlineData("contoso!Parser::Parse(char const *, int)+0x30", "contoso!Parser::Parse", FrameKind.Symbol)]
    [InlineData("contoso!Worker::Run$catch$0+0x1a", "contoso!Worker::Run", FrameKind.Symbol)]
    [InlineData("contoso!Worker::Run$filt$2", "contoso!Worker::Run", FrameKind.Symbol)]
    [InlineData("contoso!Widget::Draw+0x8 [c:\\src\\widget.cpp @ 120]", "contoso!Widget::Draw", FrameKind.Symbol)]
    [InlineData("contoso!`anonymous namespace'::Helper+0x3", "contoso!`anonymous namespace'::Helper", FrameKind.Symbol)]
    [InlineData("Contoso+0x0001A2B0", "contoso+0x1a2b0", FrameKind.Offset)]
    [InlineData("<Unloaded_dbghelp.dll>+0x1000", "<unloaded_dbghelp.dll>+0x1000", FrameKind.Offset)]
    [InlineData("0x00007ff6`1a2b3c44", "<unknown>", FrameKind.Unknown)]
    [InlineData("0x7ff61a2b3c44", "<unknown>", FrameKind.Unknown)]
    public void NormalizeCallSite_ReducesToModuleAndFunction(string callSite, string expected, FrameKind kind)
    {
        var frame = CrashFingerprinter.NormalizeCallSite(callSite)!;

        Assert.Equal(expected, frame.Token);
        Assert.Equal(kind, frame.Kind);
    }

    [Fact]
    public void ParseStack_SkipsRegistersAndDisassembly()
    {
        var frames = CrashFingerprinter.ParseStack(AccessViolationStack);

        Assert.Equal(8, frames.Count);
        Assert.Equal("crashscenarios!DumpAnalysisService_CrashScenarios_Scenarios__ReadThroughPointer", frames[0].Token);
        Assert.Equal("ntdll!RtlUserThreadStart", frames[^1].Token);
    }

    [Fact]
    public void ParseStack_ReadsX86AndInlineFrames()
    {
        const string stack = """
             # ChildEBP RetAddr
            00 0019f8c4 00401235     contoso!Inner+0x5
            01 (Inline Function) --------     contoso!Middle+0x1
            02 0019f8d0 77b1c123     contoso!Outer+0x10
            """;

        var tokens = CrashFingerprinter.ParseStack(stack).Select(f => f.Token).ToList();

        Assert.Equal(["contoso!Inner", "contoso!Middle", "contoso!Outer"], tokens);
    }

    // ---- fingerprints

    [Fact]
    public void SameCrash_DifferentAslrAndRebuild_HasTheSameFingerprint()
    {
        var first = Fingerprinter.Compute("0xC0000005", CrashFingerprinter.ParseStack(AccessViolationStack));
        var second = Fingerprinter.Compute("0xC0000005", CrashFingerprinter.ParseStack(AccessViolationStackRebuilt));

        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(12, first.Fingerprint.Length);
        Assert.Equal(CrashFingerprinter.Exact, first.Quality);
    }

    [Fact]
    public void Fingerprint_UsesTheFirstFiveMeaningfulFrames_WithoutThreadStartFrames()
    {
        var result = Fingerprinter.Compute("0xC0000005", CrashFingerprinter.ParseStack(AccessViolationStack));

        Assert.Equal(5, result.Frames.Count);
        Assert.Equal("crashscenarios!DumpAnalysisService_CrashScenarios_Scenarios__ReadThroughPointer", result.Frames[0]);
        Assert.DoesNotContain(result.Frames, f => f.StartsWith("kernel32!") || f.StartsWith("ntdll!"));
        Assert.StartsWith("0XC0000005\n", result.HashInput);
    }

    [Fact]
    public void SameFaultingFunction_DifferentCallers_DifferentFingerprints()
    {
        var direct = Fingerprinter.Compute("0xC0000005", CrashFingerprinter.ParseStack(AccessViolationStack));
        var alternate = Fingerprinter.Compute("0xC0000005", CrashFingerprinter.ParseStack(AlternatePathStack));

        Assert.NotEqual(direct.Fingerprint, alternate.Fingerprint);
    }

    [Fact]
    public void SameStack_DifferentExceptionCode_DifferentFingerprints()
    {
        var frames = CrashFingerprinter.ParseStack(AccessViolationStack);

        Assert.NotEqual(Fingerprinter.Compute("0xC0000005", frames).Fingerprint, Fingerprinter.Compute("0xC0000094", frames).Fingerprint);
    }

    [Fact]
    public void CppThrow_DropsTheThrowMachinery()
    {
        var result = Fingerprinter.Compute("0xE06D7363", CrashFingerprinter.ParseStack(CppThrowStack));

        Assert.Equal("crashscenarios!DumpAnalysisService_CrashScenarios_Scenarios__ThrowFromNativeLibrary", result.Frames[0]);
        Assert.DoesNotContain(result.Frames, f => f.Contains("RaiseException") || f.Contains("_CxxThrowException") || f.Contains("_Xout_of_range"));
    }

    [Fact]
    public void Recursion_IsCollapsed_AndItsDepthDoesNotChangeTheFingerprint()
    {
        var deep = Fingerprinter.Compute("0xC00000FD", CrashFingerprinter.ParseStack(_Recursion(100)));
        var shallow = Fingerprinter.Compute("0xC00000FD", CrashFingerprinter.ParseStack(_Recursion(40)));

        Assert.Equal("crashscenarios!DumpAnalysisService_CrashScenarios_Scenarios__RecurseWithoutLimit (×100)", Assert.Single(deep.Frames));
        Assert.Equal(deep.Fingerprint, shallow.Fingerprint);
        Assert.Equal(CrashFingerprinter.Exact, deep.Quality); // a single recursive frame still identifies a stack overflow
    }

    [Fact]
    public void FramesByOffset_ArePartial_AndNoSymbolsAtAllIsWeak()
    {
        const string partial = """
             # Child-SP          RetAddr               Call Site
            00 000000c3`77cff6b8 00007ff6`1a2b3c61     contoso+0x1a2b
            01 000000c3`77cff6c0 00007ff6`1a2b3c8a     contoso!Worker::Run+0x11
            """;
        const string none = """
             # Child-SP          RetAddr               Call Site
            00 000000c3`77cff6b8 00007ff6`1a2b3c61     contoso+0x1a2b
            01 000000c3`77cff6c0 00007ff6`1a2b3c8a     contoso+0x3c4d
            02 000000c3`77cff6f0 00007ff6`1a2b3cb3     0x7ff61a2b3c44
            """;

        Assert.Equal(CrashFingerprinter.Partial, Fingerprinter.Compute("0xC0000005", CrashFingerprinter.ParseStack(partial)).Quality);
        Assert.Equal(CrashFingerprinter.Weak, Fingerprinter.Compute("0xC0000005", CrashFingerprinter.ParseStack(none)).Quality);
    }

    [Fact]
    public void SingleMeaningfulFrame_IsWeak()
    {
        const string stack = """
             # Child-SP          RetAddr               Call Site
            00 000000c3`77cff6b8 00007ff6`1a2b3c61     KERNELBASE!RaiseException+0x6c
            01 000000c3`77cff6c0 00007ff6`1a2b3c8a     contoso!Main+0x11
            02 000000c3`77cff6f0 00007ff6`1a2b3cb3     KERNEL32!BaseThreadInitThunk+0x17
            """;

        Assert.Equal(CrashFingerprinter.Weak, Fingerprinter.Compute("0xE0001234", CrashFingerprinter.ParseStack(stack)).Quality);
    }

    [Fact]
    public void UnknownFrames_AreCollapsedIntoOne()
    {
        const string stack = """
             # Child-SP          RetAddr               Call Site
            00 000000c3`77cff6b8 00007ff6`1a2b3c61     0x7ff61a2b3c44
            01 000000c3`77cff6c0 00007ff6`1a2b3c8a     0x7ff61a2b3d00
            02 000000c3`77cff6f0 00007ff6`1a2b3cb3     coreclr!CallDescrWorkerInternal+0x83
            03 000000c3`77cff720 00007ff6`1a2b3cb3     coreclr!MethodDescCallSite::CallTargetWorker+0x268
            """;

        var result = Fingerprinter.Compute("0xE0434352", CrashFingerprinter.ParseStack(stack));

        Assert.Equal(["<unknown>", "coreclr!CallDescrWorkerInternal", "coreclr!MethodDescCallSite::CallTargetWorker"], result.Frames);
        Assert.Equal(CrashFingerprinter.Partial, result.Quality);
    }

    // ---- rules

    [Fact]
    public void Rules_BuiltInLoad_AndIgnoreOffsetFramesByModule()
    {
        var rules = FingerprintRules.Load();

        Assert.Equal(FingerprintRules.DefaultFrameCount, rules.FrameCount);
        Assert.Contains("kernelbase!RaiseException", rules.IgnoreFrames);
        Assert.True(rules.IsIgnored(CrashFingerprinter.NormalizeCallSite("KERNELBASE!RaiseException+0x6c")!));
        Assert.True(rules.IsIgnored(CrashFingerprinter.NormalizeCallSite("faultrep+0x1234")!));
        Assert.False(rules.IsIgnored(CrashFingerprinter.NormalizeCallSite("contoso!Main+0x1")!));
        Assert.False(rules.IsIgnored(CrashFingerprinter.NormalizeCallSite("0x7ff61a2b3c44")!));
    }

    [Fact]
    public void Rules_FromFile_WithFrameCountOverride()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rules-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{ "frameCount": 2, "ignoreFrames": ["contoso!Log*"] }""");
        try
        {
            var fromFile = FingerprintRules.Load(new FingerprintOptions { RulesPath = path });
            var overridden = FingerprintRules.Load(new FingerprintOptions { RulesPath = path, FrameCount = 7 });

            Assert.Equal(2, fromFile.FrameCount);
            Assert.Equal(7, overridden.FrameCount);
            Assert.True(fromFile.IsIgnored(CrashFingerprinter.NormalizeCallSite("contoso!LogError+0x10")!));
            Assert.False(fromFile.IsIgnored(CrashFingerprinter.NormalizeCallSite("KERNELBASE!RaiseException")!));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65)]
    public void Rules_ImplausibleFrameCount_Throws(int frameCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FingerprintRules(frameCount, []));
    }
}
