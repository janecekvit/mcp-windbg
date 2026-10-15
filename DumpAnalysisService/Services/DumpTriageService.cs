using System.Security.Cryptography;
using DumpAnalysisService.Infrastructure.Minidump;
using DumpAnalysisService.Security;
using Microsoft.Extensions.Options;
using Shared.Configuration;
using Shared.Models;

namespace DumpAnalysisService.Services;

/// <summary>
/// Builds a <see cref="DumpTriageResult"/> from a dump's minidump streams: no debugger,
/// no symbols, typically well under a second.
/// </summary>
public sealed class DumpTriageService : IDumpTriageService
{
    /// <summary>Files larger than this are not hashed; hashing several GB would dominate triage time.</summary>
    public const long MaxHashedFileSize = 1L << 30;

    /// <summary>Open handles above which a handle leak is suspected.</summary>
    public const int HandleLeakThreshold = 10_000;

    /// <summary>A crash within this time after process start is treated as a startup failure.</summary>
    public static readonly TimeSpan StartupWindow = TimeSpan.FromSeconds(10);

    private const ulong NullPageEnd = 0x10000;

    private readonly ILogger<DumpTriageService> _logger;
    private readonly SecurityOptions _securityOptions;
    private readonly DebuggerConfiguration? _debuggerConfig;

    public DumpTriageService(
        ILogger<DumpTriageService> logger,
        IOptions<SecurityOptions> securityOptions,
        DebuggerConfiguration? debuggerConfig = null)
    {
        _logger = logger;
        _securityOptions = securityOptions.Value;
        _debuggerConfig = debuggerConfig;
    }

    public async Task<DumpTriageResult> TriageAsync(string dumpFilePath, string? symbolCache = null, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(dumpFilePath))
            throw new FileNotFoundException($"Dump file not found: {dumpFilePath}", dumpFilePath);

        DumpPathPolicy.EnsureAllowed(dumpFilePath, _securityOptions);
        var cache = _ResolveSymbolCache(symbolCache);

        await using var stream = new FileStream(dumpFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
        var data = MinidumpParser.Parse(stream);

        string? sha256 = null;
        if (stream.Length <= MaxHashedFileSize)
        {
            stream.Position = 0;
            sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
        }

        var result = Build(dumpFilePath, stream.Length, sha256, data);
        if (cache is not null && !data.IsKernelDump)
            result = WithSymbolStatus(result, data, cache);

        _logger.LogInformation("Triaged {DumpFile}: {Format}, fault {Code} ({Class}) in {Module}",
            dumpFilePath, result.Dump.Format, result.Fault?.Code ?? "none", result.Fault?.Classification ?? "-", result.Fault?.Module ?? "-");
        return result;
    }

    /// <summary>The cache load_dump would use: the client's (if overrides are allowed), else the server default.</summary>
    private string? _ResolveSymbolCache(string? clientCache)
    {
        if (!string.IsNullOrWhiteSpace(clientCache))
        {
            if (_securityOptions.AllowClientSymbolOverrides)
            {
                SymbolInputValidator.Validate(new SymbolsConfiguration(SymbolCache: clientCache));
                return Environment.ExpandEnvironmentVariables(clientCache);
            }
            _logger.LogWarning("Ignoring client symbol cache for triage (Security:AllowClientSymbolOverrides=false)");
        }

        var configured = _debuggerConfig?.GetSymbolCachePath();
        return string.IsNullOrWhiteSpace(configured) ? null : Environment.ExpandEnvironmentVariables(configured);
    }

    /// <summary>Pure mapping from parsed streams to the triage result (unit-testable without files).</summary>
    public static DumpTriageResult Build(string path, long sizeBytes, string? sha256, MinidumpData data)
    {
        if (data.Kernel is { } kernel)
            return _BuildKernel(path, sizeBytes, sha256, kernel);

        var architecture = data.SystemInfo is { } sys ? _ArchitectureName(sys.ProcessorArchitecture) : null;
        var wow64 = architecture is "x64" or "arm64" && data.Modules.Any(m => _Wow64Modules.Contains(_FileName(m.Path)));
        var bitness = wow64 ? 32 : architecture switch { "x86" or "arm" => 32, "x64" or "arm64" or "ia64" => 64, _ => (int?)null };
        var debugRuntime = data.Modules.Any(m => _DebugRuntimeModules.Contains(_FileName(m.Path)));
        var faultingModule = data.Exception is { } ex ? data.Modules.FirstOrDefault(m => m.Contains(ex.Address)) : null;
        var mainModule = data.Modules.Count > 0 ? data.Modules[0] : null;

        var system = data.SystemInfo is { } s
            ? new DumpSystemInfo(
                architecture!,
                s.NumberOfProcessors,
                $"{s.MajorVersion}.{s.MinorVersion}.{s.BuildNumber}",
                s.ProductType switch { 1 => "workstation", 2 => "domain-controller", 3 => "server", _ => null })
            : null;

        var startedUtc = data.MiscInfo?.ProcessCreateTime;
        var process = new DumpProcessInfo(
            ImageName: mainModule is null ? null : _FileName(mainModule.Path),
            ImagePath: mainModule?.Path,
            ProcessId: data.MiscInfo?.ProcessId,
            Bitness: bitness,
            StartedUtc: startedUtc,
            Uptime: startedUtc is { } start && data.TimeDateStamp is { } written && written >= start ? written - start : null,
            CpuTime: data.MiscInfo is { UserTime: { } user, KernelTime: { } kernelTime } ? user + kernelTime : null,
            Wow64: wow64,
            DebugRuntime: debugRuntime);

        var fault = data.Exception is { } e ? _BuildFault(data, e, faultingModule, bitness == 64 && !wow64) : null;

        var windowsModules = data.Modules.Count(m => _IsWindowsModule(m.Path));
        var notable = data.Modules
            .Where(m => ReferenceEquals(m, faultingModule) || !_IsWindowsModule(m.Path))
            .Take(50)
            .Select(m => _ToModuleInfo(m, ReferenceEquals(m, faultingModule)))
            .ToList();
        var unloaded = data.UnloadedModules
            .Take(64)
            .Select(m => new DumpUnloadedModuleInfo(_FileName(m.Path), _Hex(m.BaseAddress), m.Size,
                m.TimeDateStamp == 0 ? null : DateTimeOffset.FromUnixTimeSeconds(m.TimeDateStamp)))
            .ToList();

        var result = new DumpTriageResult(
            Dump: new DumpFileInfo(path, sizeBytes, "user-minidump", sha256, data.HasFullMemory, data.TimeDateStamp, _Contents(data), data.Comment),
            System: system,
            Process: process,
            Fault: fault,
            Threads: _BuildThreads(data),
            Modules: new DumpModuleSummary(data.Modules.Count, windowsModules, notable, unloaded),
            Runtime: _DetectRuntime(data.Modules),
            Memory: _BuildMemory(data, bitness),
            Handles: data.Handles is { } h
                ? new DumpHandleSummary(h.Count, h.ByType.OrderByDescending(t => t.Value).ThenBy(t => t.Key, StringComparer.Ordinal)
                    .Take(10).Select(t => new DumpHandleTypeCount(t.Key, t.Value)).ToList())
                : null,
            Symbols: null,
            Kernel: null,
            Hints: []);

        return result with { Hints = _BuildHints(result, data, faultingModule) };
    }

    // ---- fault

    private static DumpFaultInfo _BuildFault(MinidumpData data, MinidumpException exception, MinidumpModule? module, bool is64)
    {
        string? accessType = null;
        ulong? target = null;
        if (exception.Code is ExceptionCodes.AccessViolation or ExceptionCodes.InPageError && exception.Parameters.Count >= 2)
        {
            accessType = exception.Parameters[0] switch { 0 => "read", 1 => "write", 8 => "execute", _ => null };
            target = exception.Parameters[1];
        }

        var unloaded = data.UnloadedModules.FirstOrDefault(u => u.Contains(exception.Address))
            ?? (target is { } t ? data.UnloadedModules.FirstOrDefault(u => u.Contains(t)) : null);
        var targetRegion = target is { } tr ? data.RegionOf(tr) : null;

        var suspicious = new List<DumpSuspiciousValue>();
        if (target is { } tv && FillPatterns.Match(tv) is { } targetPattern)
            suspicious.Add(new DumpSuspiciousValue("target", _Hex(tv), targetPattern.Meaning));
        foreach (var register in exception.Registers.Where(r => r.Name is not ("rip" or "eip" or "rsp" or "esp")))
        {
            if (FillPatterns.Match(register.Value) is { } pattern)
                suspicious.Add(new DumpSuspiciousValue(register.Name, _Hex(register.Value), pattern.Meaning));
        }

        string? fastFail = null;
        if (exception.Code == ExceptionCodes.FailFast && exception.Parameters.Count >= 1)
            fastFail = FastFailCodes.NameOf(exception.Parameters[0]) ?? $"0x{exception.Parameters[0]:x}";

        var classification = exception.Code == ExceptionCodes.AccessViolation
            ? _ClassifyAccessViolation(exception, accessType, target, unloaded, targetRegion, is64)
            : ExceptionCodes.ClassOf(exception.Code);

        return new DumpFaultInfo(
            Code: $"0x{exception.Code:X8}",
            CodeName: ExceptionCodes.NameOf(exception.Code),
            Classification: classification,
            ThreadId: exception.ThreadId,
            ThreadName: data.ThreadNames.GetValueOrDefault(exception.ThreadId),
            Address: _Hex(exception.Address),
            Module: module is null ? null : _ModuleName(module.Path),
            ModuleOffset: module is null ? null : _Hex(exception.Address - module.BaseAddress),
            UnloadedModule: unloaded is null ? null : _FileName(unloaded.Path),
            AccessType: accessType,
            TargetAddress: target is { } ta ? _Hex(ta) : null,
            TargetRegion: targetRegion is null ? null : _ToRegion(targetRegion),
            FastFailCode: fastFail,
            Parameters: exception.Parameters.Select(_Hex).ToList(),
            Registers: exception.Registers.ToDictionary(r => r.Name, r => _Hex(r.Value)),
            SuspiciousValues: suspicious,
            CppException: data.CppException is { } cpp
                ? new DumpCppExceptionInfo(cpp.TypeNames[0], cpp.TypeNames, cpp.Message)
                : null,
            DelayLoad: data.DelayLoad is { } dl ? new DumpDelayLoadInfo(dl.Dll, dl.Procedure, dl.LastError) : null);
    }

    private static string _ClassifyAccessViolation(
        MinidumpException exception, string? access, ulong? target, MinidumpUnloadedModule? unloaded, MinidumpMemoryRegion? region, bool is64)
    {
        if (access == "execute" && unloaded is not null && unloaded.Contains(exception.Address))
            return "call-into-unloaded-module";
        if (target is not { } t)
            return "access-violation";
        if (t < NullPageEnd)
            return access == "execute" ? "null-function-pointer" : "null-pointer";
        // A repeated 64-bit fill value is itself non-canonical; the pattern says more.
        if (FillPatterns.Match(t) is { } pattern)
            return pattern.Classification;
        if (is64 && t is >= 0x0000_8000_0000_0000 and < 0xFFFF_8000_0000_0000 || t == ulong.MaxValue && is64)
            return "non-canonical-address";
        if (unloaded is not null && unloaded.Contains(t))
            return "access-to-unloaded-module";

        if (region is not null)
        {
            if (region.State == MemoryStates.Free)
                return "unallocated-address";
            if (region.State == MemoryStates.Reserve)
                return "reserved-not-committed";
            if ((region.Protect & Protection.Guard) != 0)
                return "guard-page";
            var basic = region.Protect & 0xFF;
            if (basic == Protection.NoAccess)
                return "no-access-page";
            if (access == "execute" && !Protection.IsExecutable(basic))
                return "execute-non-executable";
            if (access == "write" && !Protection.IsWritable(basic))
                return "write-to-read-only";
        }

        return access == "execute" ? "wild-execute" : "wild-pointer";
    }

    // ---- threads

    private static DumpThreadSummary _BuildThreads(MinidumpData data)
    {
        var names = data.ThreadNames;
        var busiest = data.ThreadInfos
            .Where(t => !t.HasExited && t.UserTime + t.KernelTime > TimeSpan.Zero)
            .OrderByDescending(t => t.UserTime + t.KernelTime)
            .Take(5)
            .Select(t =>
            {
                var cpu = t.UserTime + t.KernelTime;
                double? share = null;
                if (t.CreateTime is { } created && data.TimeDateStamp is { } written && written > created)
                    share = Math.Round(Math.Min(1.0, cpu / (written - created)), 2);
                var startModule = data.Modules.FirstOrDefault(m => m.Contains(t.StartAddress));
                return new DumpThreadInfo(t.ThreadId, names.GetValueOrDefault(t.ThreadId), cpu, share,
                    _Hex(t.StartAddress), startModule is null ? null : _ModuleName(startModule.Path));
            })
            .ToList();

        return new DumpThreadSummary(
            Count: data.ThreadIds.Count > 0 ? data.ThreadIds.Count : data.ThreadInfos.Count,
            FaultingThreadId: data.Exception?.ThreadId,
            Exited: data.ThreadInfos.Count(t => t.HasExited),
            Busiest: busiest,
            Named: names.OrderBy(n => n.Key).Take(64).Select(n => new DumpNamedThread(n.Key, n.Value)).ToList());
    }

    // ---- memory

    private static DumpMemoryInfo? _BuildMemory(MinidumpData data, int? bitness)
    {
        var vm = data.VmCounters;
        var sysMem = data.SystemMemory;
        long? used = null, largestFree = null;
        if (data.MemoryRegions.Count > 0)
        {
            // A 32-bit process (also under WoW64) lives below 4 GB; ignore the 64-bit WoW64 regions above.
            var limit = bitness == 32 ? 0x1_0000_0000ul : ulong.MaxValue;
            var user = data.MemoryRegions.Where(r => r.BaseAddress < limit).ToList();
            used = (long)user.Where(r => r.State != MemoryStates.Free).Aggregate(0ul, (sum, r) => sum + r.RegionSize);
            if (bitness == 32)
                largestFree = (long)user.Where(r => r.State == MemoryStates.Free).Select(r => r.RegionSize).DefaultIfEmpty().Max();
        }

        if (vm is null && sysMem is null && used is null)
            return null;

        return new DumpMemoryInfo(
            PrivateBytes: (long?)vm?.PrivateUsage,
            PeakPrivateBytes: (long?)vm?.PeakPrivateUsage,
            WorkingSet: (long?)vm?.WorkingSetSize,
            PeakWorkingSet: (long?)vm?.PeakWorkingSetSize,
            VirtualSize: (long?)vm?.VirtualSize,
            JobCommitLimit: (long?)vm?.JobPrivateCommitLimit,
            AddressSpaceUsed: used,
            LargestFreeRegion: largestFree,
            SystemCommitUsed: (long?)sysMem?.CommittedBytes,
            SystemCommitLimit: (long?)sysMem?.CommitLimitBytes,
            SystemPhysicalMemory: (long?)sysMem?.PhysicalMemoryBytes);
    }

    // ---- hints

    private static List<string> _BuildHints(DumpTriageResult result, MinidumpData data, MinidumpModule? faultingModule)
    {
        var hints = new List<string>();
        var fault = result.Fault;
        var process = result.Process!;

        if (fault is null)
        {
            hints.Add("No exception record: this is a snapshot (hang or on-demand dump). Look at the thread stacks with predefined_analysis \"threads\" and at locks with \"locks\".");
        }
        else
        {
            hints.Add(_FaultHint(fault, data));

            if (fault.Module is null && fault.Classification is not ("call-into-unloaded-module" or "null-function-pointer" or "execute-non-executable")
                && data.Exception!.Code is not (ExceptionCodes.ClrException or ExceptionCodes.FailFastException))
                hints.Add("The faulting address is outside every loaded module: JIT-compiled code, a corrupted return address or an unloaded module.");

            var registers = fault.SuspiciousValues.Where(v => v.Location != "target").ToList();
            if (registers.Count > 0)
                hints.Add("Registers holding fill patterns: " + string.Join(", ", registers.Select(r => $"{r.Location}={r.Value} ({r.Meaning})")) + ".");

            if (process.Uptime is { } uptime && uptime < StartupWindow && data.Exception!.Code != ExceptionCodes.Breakpoint)
                hints.Add($"The process crashed {uptime.TotalSeconds:0} s after it started: a startup failure (missing dependency, configuration or first-run path).");
        }

        if (faultingModule?.CodeView is { } cv)
            hints.Add($"Symbols for {_ModuleName(faultingModule.Path)}: {_FileName(cv.PdbPath)} (key {_SymbolKey(cv)}) must be on the symbol path for named frames.");

        if (process.Wow64)
            hints.Add("32-bit process in a 64-bit dump (WoW64): in cdb switch with .effmach x86 (or !wow64exts.sw) to see the 32-bit stacks; a 32-bit dumper gives a cleaner dump.");

        if (process.DebugRuntime)
            hints.Add("A debug C runtime is loaded (debug build): heap fill patterns apply, 0xDD freed, 0xCD uninitialised, 0xFD guard bytes.");

        _AddThreadHints(hints, result, data);
        _AddMemoryHints(hints, result, data);

        if (result.Handles is { Count: >= HandleLeakThreshold } handles)
        {
            var top = handles.ByType.FirstOrDefault();
            hints.Add($"{handles.Count:N0} open handles{(top is null ? "" : $" (most: {top.Type} ×{top.Count:N0})")}: a handle leak is likely; run predefined_analysis \"handles\".");
        }

        if (!data.HasFullMemory)
            hints.Add("Mini dump without full memory: heap and object inspection will be limited.");
        if (!result.Dump.Contents.Contains("handle-data"))
            hints.Add("No handle data in the dump: predefined_analysis \"handles\" will be empty (procdump -ma and .dump /ma include it).");

        if (data.Comment is { } comment)
            hints.Add($"Dumper comment: \"{(comment.Length > 200 ? comment[..200] + "…" : comment)}\".");

        return hints;
    }

    private static string _FaultHint(DumpFaultInfo fault, MinidumpData data)
    {
        var where = fault.Module is null ? $"at {fault.Address}" : $"in {fault.Module}+{fault.ModuleOffset}";
        var exception = data.Exception!;
        var access = fault.AccessType ?? "access";
        var target = fault.TargetAddress ?? "an unknown address";

        switch (exception.Code)
        {
            case ExceptionCodes.AccessViolation:
                var pattern = fault.SuspiciousValues.FirstOrDefault(v => v.Location == "target");
                return fault.Classification switch
                {
                    "null-pointer" => $"Null-pointer {access} (target {target}) {where}. Run basic_analysis for the faulting stack.",
                    "null-function-pointer" => $"Call through a null function pointer (execution at {target}): an uninitialised callback, vtable or function pointer. Run basic_analysis to see the caller.",
                    "call-into-unloaded-module" => $"Execution at {fault.Address}, inside the old range of the unloaded module {fault.UnloadedModule}: code called into the DLL after it was unloaded (a dangling callback, thread, timer or hook).",
                    "access-to-unloaded-module" => $"{_Capitalize(access)} of {target} {where}, inside the old range of the unloaded module {fault.UnloadedModule}: a dangling pointer to that module's data.",
                    "non-canonical-address" => $"{_Capitalize(access)} through a non-canonical address {where} (x64 reports {target}; the real address is not recorded): a garbage pointer, e.g. read from freed or uninitialised memory. Check the registers.",
                    "use-after-free-pattern" or "uninitialized-memory-pattern" or "buffer-overrun-pattern" =>
                        $"{_Capitalize(access)} of {target} {where}: the address is a fill pattern, {pattern?.Meaning}. {_PatternVerdict(fault.Classification)}",
                    "unallocated-address" => $"{_Capitalize(access)} of {target} {where}: nothing is allocated there (never allocated or released with VirtualFree): a wild or dangling pointer.",
                    "reserved-not-committed" => $"{_Capitalize(access)} of {target} {where}: the address is reserved but not committed (past the committed part of a stack or a sparse buffer; with page heap enabled, freed memory).",
                    "guard-page" => $"{_Capitalize(access)} of {target} {where} hit a guard page (stack probing or a guard the application set).",
                    "no-access-page" => $"{_Capitalize(access)} of {target} {where} hit a PAGE_NOACCESS page (with page heap enabled, the guard page after a block: a buffer overrun).",
                    "execute-non-executable" => $"Execution of non-executable memory at {target} ({fault.TargetRegion?.Protect}) (DEP): a corrupt function pointer or return address.",
                    "write-to-read-only" => $"Write to read-only memory at {target} ({fault.TargetRegion?.Protect}, {fault.TargetRegion?.Type}) {where}: writing to a string literal, constant data or code.",
                    _ => $"Access violation ({access} of {target}) {where}. A wild or freed pointer is likely; run basic_analysis.",
                };
            case ExceptionCodes.InPageError:
                var status = exception.Parameters.Count >= 3 ? $" (status 0x{exception.Parameters[2]:X8})" : "";
                return $"In-page I/O error {where} while paging in {target}{status}: the file, network share or disk behind a memory-mapped file or image failed.";
            case ExceptionCodes.StackOverflow:
                return $"Stack overflow {where}. Look for a repeating frame (recursion) in the faulting thread's stack.";
            case ExceptionCodes.IntegerDivideByZero:
                return $"Integer division by zero {where}. Run basic_analysis for the faulting stack.";
            case ExceptionCodes.ClrException:
                return ".NET exception. Load the dump and use SOS (!pe, !clrstack) to see the managed exception and stack.";
            case ExceptionCodes.CppException:
                return fault.CppException is { } cpp
                    ? $"Unhandled C++ exception {cpp.Type}{(cpp.Message is null ? "" : $": \"{cpp.Message}\"")}. The throwing frame is below KERNELBASE!RaiseException on the faulting stack."
                    : "Unhandled C++ exception (throw). The throwing frame is below KERNELBASE!RaiseException on the faulting stack.";
            case ExceptionCodes.FailFast or ExceptionCodes.FailFastException:
                var meaning = exception.Code == ExceptionCodes.FailFast && exception.Parameters.Count >= 1
                    ? FastFailCodes.Meaning(exception.Parameters[0])
                    : null;
                return fault.FastFailCode is { } code
                    ? $"Fail-fast {code} {where}: {meaning ?? "the process terminated itself after a failed check"}."
                    : $"Fail-fast {where}: the process terminated itself (security check, Environment.FailFast or __fastfail). Check the caller of the fail-fast.";
            case ExceptionCodes.HeapCorruption:
                return "Heap corruption detected by the heap manager. Enable page heap (gflags /p /enable) and reproduce for the real culprit.";
            case ExceptionCodes.Breakpoint:
                return "Break instruction: the dump was probably taken by a debugger or a hang monitor. Treat it as a snapshot and inspect all threads.";
            case ExceptionCodes.Wow64Breakpoint or ExceptionCodes.Wow64SingleStep:
                return "WoW64 break or single step: a 32-bit process was dumped by a 64-bit debugger at a break; treat it as a snapshot and switch to the 32-bit view with .effmach x86.";
            case ExceptionCodes.NoMemory:
                return $"Out of memory (STATUS_NO_MEMORY) {where}: see the memory section for commit and address space usage.";
            case ExceptionCodes.IllegalInstruction:
                return $"Illegal instruction {where}: code for a CPU feature this machine lacks (e.g. AVX2 or AVX-512 on an older CPU or VM), a deliberate ud2 trap, or execution of data.";
            case ExceptionCodes.PrivilegedInstruction:
                return $"Privileged instruction {where}: execution of data or of a corrupt code pointer.";
            case ExceptionCodes.DllNotFound or ExceptionCodes.EntryPointNotFound or ExceptionCodes.DllInitFailed or 0xC000007B:
                return $"{fault.CodeName}: a DLL dependency is missing, mismatched (wrong bitness or version) or its DllMain failed. Check the dependencies (VC++ redistributable, PATH, side-by-side manifests).";
            case ExceptionCodes.DelayLoadModuleNotFound or ExceptionCodes.DelayLoadProcNotFound:
                var error = fault.DelayLoad?.LastError is { } le and not 0 ? $" (error {le})" : "";
                return fault.DelayLoad is { } dl
                    ? exception.Code == ExceptionCodes.DelayLoadModuleNotFound
                        ? $"The delay-loaded DLL {dl.Dll} could not be loaded at its first use{error}: it is missing or a dependency of it is."
                        : $"The function {dl.Procedure} was not found in the delay-loaded {dl.Dll}{error}: a version mismatch of that DLL."
                    : "A delay-loaded DLL or function could not be resolved at its first use: a missing or mismatched DLL.";
            case ExceptionCodes.InvalidCRuntimeParameter:
                return $"The CRT invalid parameter handler ended the process {where}: a CRT function got a null pointer, an invalid format string or a too small buffer (the *_s functions).";
            case ExceptionCodes.FatalAppExit:
                return $"abort() ended the process {where}: often std::terminate after an unhandled C++ exception, or a failed assert().";
            case ExceptionCodes.AssertionFailure:
                return $"An assertion failed {where} (__assert / int 2c).";
            case ExceptionCodes.PossibleDeadlock:
                return $"A critical section wait timed out (STATUS_POSSIBLE_DEADLOCK) {where}: a deadlock; run predefined_analysis \"locks\".";
            case ExceptionCodes.FatalUserCallbackException:
                return $"An exception escaped a user-mode callback from the kernel (window procedure, hook or APC) {where}; the original exception is nested inside this one.";
            case ExceptionCodes.StowedException:
                return $"WinRT stowed exception {where}: the original errors are in the stowed exception array (PDE extension !pde.dse, or SOS for .NET).";
            case ExceptionCodes.NoncontinuableException or ExceptionCodes.InvalidDisposition or ExceptionCodes.BadStack:
                return $"{fault.CodeName} {where}: the exception handling chain or the stack is corrupt.";
            case ExceptionCodes.ControlCExit:
                return "Ctrl+C or closing the console ended the process: not a crash.";
            case ExceptionCodes.SetThreadName:
                return "This is the first-chance SetThreadName exception, which is harmless: the dump was taken at the wrong event.";
            default:
                return $"{fault.CodeName} ({fault.Code}) {where}. Run basic_analysis for the faulting stack.";
        }
    }

    private static string _PatternVerdict(string classification) => classification switch
    {
        "use-after-free-pattern" => "The pointer came from freed memory: a use after free.",
        "uninitialized-memory-pattern" => "The pointer was never initialised.",
        _ => "The pointer came from guard bytes after a block: a buffer overrun.",
    };

    private static void _AddThreadHints(List<string> hints, DumpTriageResult result, MinidumpData data)
    {
        var threads = result.Threads!;
        var busy = threads.Busiest.FirstOrDefault(t => t.CpuShare >= 0.5 && t.CpuTime >= TimeSpan.FromSeconds(1));
        if (busy is not null)
        {
            hints.Add($"Thread {busy.Id}{(busy.Name is null ? "" : $" ({busy.Name})")} used {busy.CpuTime.TotalSeconds:0.#} s of CPU, " +
                $"{busy.CpuShare:P0} of its lifetime: a busy loop or livelock. Look at that thread's stack (predefined_analysis \"threads\").");
        }
        else if (result.Fault is null or { Classification: "breakpoint" } && data.ThreadInfos.Count > 0
            && threads.Busiest.All(t => t.CpuShare is null or < 0.1))
        {
            hints.Add("No thread was busy (each used under 10% CPU over its lifetime): the process was waiting, a deadlock, blocked I/O or a lost wake-up; run predefined_analysis \"locks\".");
        }
    }

    private static void _AddMemoryHints(List<string> hints, DumpTriageResult result, MinidumpData data)
    {
        if (result.Memory is not { } memory)
            return;

        if (result.Process!.Bitness == 32)
        {
            // Without LARGEADDRESSAWARE a 32-bit process gets 2 GB; any region above that means it has more.
            var limit = data.MemoryRegions.Any(r => r.BaseAddress >= 0x8000_0000 && r.BaseAddress < 0x1_0000_0000 && r.State != MemoryStates.Free)
                ? 4L << 30
                : 2L << 30;
            var usedSpace = memory.VirtualSize is { } vs && vs < limit * 2 ? vs : memory.AddressSpaceUsed;
            if (usedSpace >= limit * 9 / 10 || memory.LargestFreeRegion is < 64L << 20 and > 0)
            {
                hints.Add($"The 32-bit address space is nearly exhausted ({_Size(usedSpace)} of {_Size(limit)} used, largest free block " +
                    $"{_Size(memory.LargestFreeRegion)}): allocations fail even with free RAM. Fix the leak, or build 64-bit or LARGEADDRESSAWARE.");
            }
        }

        if (memory is { JobCommitLimit: { } jobLimit, PrivateBytes: { } jobPrivate } && jobPrivate >= jobLimit * 9 / 10)
            hints.Add($"The process is at {jobPrivate * 100 / jobLimit}% of its job object commit limit ({_Size(jobLimit)}): allocations fail at the limit.");

        if (memory is { SystemCommitLimit: > 0 and var commitLimit, SystemCommitUsed: { } commitUsed } && commitUsed >= commitLimit * 9 / 10)
            hints.Add($"System commit charge was {commitUsed * 100 / commitLimit}% of the limit ({_Size(commitUsed)} of {_Size(commitLimit)}): the whole machine was running out of virtual memory.");

        if (memory is { PrivateBytes: { } privateBytes, SystemPhysicalMemory: > 0 and var physical } && privateBytes >= physical / 2)
            hints.Add($"Private bytes are {_Size(privateBytes)}, {privateBytes * 100 / physical}% of physical memory: a memory leak is likely.");
    }

    // ---- symbols

    /// <summary>
    /// Adds which PDBs are already in the symbol cache (layout cache\name.pdb\KEY\name.pdb, also the
    /// two-tier layout and compressed or file.ptr entries), so the caller knows whether load_dump will
    /// download symbols for minutes.
    /// </summary>
    public static DumpTriageResult WithSymbolStatus(DumpTriageResult result, MinidumpData data, string cachePath)
    {
        var twoTier = File.Exists(Path.Combine(cachePath, "index2.txt"));
        bool InCache(MinidumpCodeView cv)
        {
            var name = _FileName(cv.PdbPath);
            if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return false;
            var directory = twoTier && name.Length >= 2
                ? Path.Combine(cachePath, name[..2], name, _SymbolKey(cv))
                : Path.Combine(cachePath, name, _SymbolKey(cv));
            return File.Exists(Path.Combine(directory, name))
                || File.Exists(Path.Combine(directory, name[..^1] + "_"))
                || File.Exists(Path.Combine(directory, "file.ptr"));
        }

        var status = data.Modules
            .Where(m => m.CodeView is not null)
            .Select(m => (Module: m, Cached: InCache(m.CodeView!)))
            .ToList();
        if (status.Count == 0)
            return result;

        var cachedByPath = status.ToDictionary(s => s.Module.Path, s => s.Cached, StringComparer.OrdinalIgnoreCase);
        var missing = status.Where(s => !s.Cached && !_IsWindowsModule(s.Module.Path)).Select(s => _ModuleName(s.Module.Path)).ToList();
        var symbols = new DumpSymbolStatus(
            CachePath: cachePath,
            Checked: status.Count,
            Cached: status.Count(s => s.Cached),
            Missing: missing,
            WindowsMissing: status.Count(s => !s.Cached && _IsWindowsModule(s.Module.Path)));

        var hints = result.Hints.ToList();
        if (symbols.Cached == symbols.Checked)
        {
            hints.Add($"All {symbols.Checked} PDBs are already in the symbol cache: load_dump should not need to download symbols.");
        }
        else
        {
            hints.Add($"{symbols.Cached} of {symbols.Checked} PDBs are in the symbol cache ({cachePath}); load_dump will try to download the other " +
                $"{symbols.Checked - symbols.Cached} (the first load can take minutes).");
            if (missing.Count > 0)
            {
                hints.Add($"Not cached: {string.Join(", ", missing.Take(10))}{(missing.Count > 10 ? ", …" : "")}. Your own builds are not on the public symbol servers: " +
                    "point symbol_path_extra (X-Symbol-Path-Extra) at their PDBs.");
            }
        }

        return result with
        {
            Symbols = symbols,
            Modules = result.Modules! with
            {
                Notable = result.Modules.Notable
                    .Select(m => cachedByPath.TryGetValue(m.Path, out var cached) ? m with { SymbolsCached = cached } : m)
                    .ToList(),
            },
            Hints = hints,
        };
    }

    // ---- kernel

    private static DumpTriageResult _BuildKernel(string path, long sizeBytes, string? sha256, MinidumpKernelHeader kernel)
    {
        var architecture = kernel.MachineImageType switch
        {
            0x8664 => "x64",
            0x14C => "x86",
            0xAA64 => "arm64",
            _ => $"0x{kernel.MachineImageType:X}",
        };
        var code = $"0x{kernel.BugCheckCode:X8}";
        var name = BugCheckCodes.NameOf(kernel.BugCheckCode);
        var summary = BugCheckCodes.Describe(kernel.BugCheckCode, kernel.Parameters);
        var dumpType = BugCheckCodes.DumpTypeName(kernel.DumpType);

        var hints = new List<string>
        {
            $"Kernel dump, bug check {code}{(name is null ? "" : $" ({name})")}. Triage reads only the kernel dump header; load it with a kernel debugger (kd) and run !analyze -v.",
        };
        if (summary is not null)
            hints.Add(summary);
        if (kernel.DumpType == 4)
            hints.Add("Small kernel dump: only the faulting thread's stack, the bug check data and the driver list are available.");
        if (kernel.SystemUptime is { } uptime)
            hints.Add($"The system had been up for {uptime:d\\.hh\\:mm\\:ss} when it crashed.");

        return new DumpTriageResult(
            Dump: new DumpFileInfo(path, sizeBytes, "kernel-dump", sha256, FullMemory: kernel.DumpType is 1 or 5, CreatedUtc: kernel.SystemTime, Contents: [], Comment: null),
            System: null,
            Process: null,
            Fault: null,
            Threads: null,
            Modules: null,
            Runtime: null,
            Memory: null,
            Handles: null,
            Symbols: null,
            Kernel: new DumpKernelInfo(code, name, kernel.Parameters.Select(_Hex).ToList(), summary, architecture, (int)kernel.NumberProcessors,
                dumpType, kernel.SystemTime, kernel.SystemUptime),
            Hints: hints);
    }

    // ---- helpers

    private static DumpModuleInfo _ToModuleInfo(MinidumpModule module, bool isFaulting)
    {
        var cv = module.CodeView;
        return new DumpModuleInfo(
            Name: _ModuleName(module.Path),
            Path: module.Path,
            BaseAddress: _Hex(module.BaseAddress),
            Size: module.Size,
            FileVersion: module.FileVersion,
            Timestamp: module.TimeDateStamp == 0 ? null : DateTimeOffset.FromUnixTimeSeconds(module.TimeDateStamp),
            PdbName: cv is null ? null : _FileName(cv.PdbPath),
            PdbGuid: cv?.Guid.ToString("D"),
            PdbAge: cv is null ? null : (int)cv.Age,
            SymbolKey: cv is null ? null : _SymbolKey(cv),
            IsFaulting: isFaulting);
    }

    private static DumpMemoryRegion _ToRegion(MinidumpMemoryRegion region) => new(
        BaseAddress: _Hex(region.BaseAddress),
        Size: _Hex(region.RegionSize),
        State: region.State switch { MemoryStates.Commit => "commit", MemoryStates.Reserve => "reserve", MemoryStates.Free => "free", _ => _Hex(region.State) },
        Protect: region.State == MemoryStates.Free ? null : Protection.Describe(region.Protect),
        Type: region.Type switch { 0x1000000 => "image", 0x40000 => "mapped", 0x20000 => "private", 0 => null, _ => _Hex(region.Type) });

    private static DumpRuntimeInfo? _DetectRuntime(IReadOnlyList<MinidumpModule> modules)
    {
        foreach (var module in modules)
        {
            var name = _ModuleName(module.Path);
            if (name.Equals("coreclr.dll", StringComparison.OrdinalIgnoreCase))
                return new DumpRuntimeInfo(".NET", module.FileVersion);
            if (name.Equals("clr.dll", StringComparison.OrdinalIgnoreCase) || name.Equals("mscorwks.dll", StringComparison.OrdinalIgnoreCase))
                return new DumpRuntimeInfo(".NET Framework", module.FileVersion);
        }
        return null;
    }

    private static readonly Dictionary<uint, string> _streamNames = new()
    {
        [3] = "thread-list",
        [4] = "module-list",
        [5] = "memory-list",
        [6] = "exception",
        [7] = "system-info",
        [8] = "thread-ex-list",
        [9] = "memory64-list",
        [10] = "comment",
        [11] = "comment",
        [12] = "handle-data",
        [13] = "function-table",
        [14] = "unloaded-modules",
        [15] = "misc-info",
        [16] = "memory-info",
        [17] = "thread-info",
        [18] = "handle-operations",
        [19] = "token",
        [20] = "javascript-data",
        [21] = "system-memory-info",
        [22] = "process-vm-counters",
        [23] = "ipt-trace",
        [24] = "thread-names",
    };

    private static List<string> _Contents(MinidumpData data)
    {
        var contents = new List<string>();
        if (data.HasFullMemory)
            contents.Add("full-memory");
        foreach (var type in data.StreamTypes)
        {
            var name = _streamNames.GetValueOrDefault(type) ?? (type > 0xFFFF ? "user-stream" : $"stream-{type}");
            if (!contents.Contains(name))
                contents.Add(name);
        }
        return contents;
    }

    private static readonly HashSet<string> _Wow64Modules = new(StringComparer.OrdinalIgnoreCase)
    {
        "wow64.dll", "wow64cpu.dll", "wow64win.dll", "xtajit.dll",
    };

    private static readonly HashSet<string> _DebugRuntimeModules = new(StringComparer.OrdinalIgnoreCase)
    {
        "ucrtbased.dll", "vcruntime140d.dll", "vcruntime140_1d.dll", "msvcp140d.dll", "msvcr120d.dll", "msvcp120d.dll", "msvcr110d.dll", "msvcr100d.dll",
    };

    private static bool _IsWindowsModule(string path) =>
        path.Replace('/', '\\').Contains("\\Windows\\", StringComparison.OrdinalIgnoreCase);

    private static string _ModuleName(string path) => _FileName(path);

    /// <summary>The directory name a symbol server files the PDB under: GUID without dashes, then the age in hex.</summary>
    private static string _SymbolKey(MinidumpCodeView cv) => $"{cv.Guid:N}".ToUpperInvariant() + $"{cv.Age:X}";

    /// <summary>File name of a Windows path; works the same when the service runs on another OS.</summary>
    private static string _FileName(string path) => path[(path.LastIndexOfAny(['\\', '/']) + 1)..];

    private static string _ArchitectureName(ushort architecture) => architecture switch
    {
        Architectures.X86 => "x86",
        Architectures.Arm => "arm",
        Architectures.Ia64 => "ia64",
        Architectures.Amd64 => "x64",
        Architectures.Arm64 => "arm64",
        _ => $"unknown ({architecture})",
    };

    private static string _Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static string _Size(long? bytes) => bytes switch
    {
        null => "unknown",
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.#} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.#} MB",
        _ => $"{bytes / 1024.0:0.#} KB",
    };

    private static string _Hex(ulong value) => $"0x{value:x}";
}

/// <summary>Known fill values of freed, uninitialised and guard memory, as 32-bit or repeated 64-bit values (plus a small offset).</summary>
internal static class FillPatterns
{
    public sealed record Pattern(uint Value, string Classification, string Meaning);

    private static readonly Pattern[] _patterns =
    [
        new(0xDDDDDDDD, "use-after-free-pattern", "freed memory (debug CRT 0xDD fill)"),
        new(0xFEEEFEEE, "use-after-free-pattern", "freed memory (HeapFree 0xFEEEFEEE fill)"),
        new(0xCDCDCDCD, "uninitialized-memory-pattern", "uninitialised heap memory (debug CRT 0xCD fill)"),
        new(0xBAADF00D, "uninitialized-memory-pattern", "uninitialised heap memory (HeapAlloc 0xBAADF00D fill)"),
        new(0xCCCCCCCC, "uninitialized-memory-pattern", "uninitialised stack memory (/RTC 0xCC fill)"),
        new(0xFDFDFDFD, "buffer-overrun-pattern", "guard bytes after a debug CRT block (0xFD fill)"),
        new(0xABABABAB, "buffer-overrun-pattern", "guard bytes after a HeapAlloc block (0xAB fill)"),
    ];

    /// <summary>Field offsets up to this size still count as the pattern (a member of an object at a garbage address).</summary>
    private const ulong Slack = 0x10000;

    public static Pattern? Match(ulong value)
    {
        foreach (var pattern in _patterns)
        {
            ulong p32 = pattern.Value;
            var p64 = p32 << 32 | p32;
            if (value - p32 < Slack || value - p64 < Slack)
                return pattern;
        }
        return null;
    }
}

internal static class MemoryStates
{
    public const uint Commit = 0x1000;
    public const uint Reserve = 0x2000;
    public const uint Free = 0x10000;
}

internal static class Protection
{
    public const uint NoAccess = 0x01;
    public const uint Guard = 0x100;

    private static readonly (uint Flag, string Name)[] _basic =
    [
        (0x01, "PAGE_NOACCESS"), (0x02, "PAGE_READONLY"), (0x04, "PAGE_READWRITE"), (0x08, "PAGE_WRITECOPY"),
        (0x10, "PAGE_EXECUTE"), (0x20, "PAGE_EXECUTE_READ"), (0x40, "PAGE_EXECUTE_READWRITE"), (0x80, "PAGE_EXECUTE_WRITECOPY"),
    ];

    public static bool IsExecutable(uint basic) => basic is 0x10 or 0x20 or 0x40 or 0x80;

    public static bool IsWritable(uint basic) => basic is 0x04 or 0x08 or 0x40 or 0x80;

    public static string? Describe(uint protect)
    {
        if (protect == 0)
            return null;
        var parts = _basic.Where(b => b.Flag == (protect & 0xFF)).Select(b => b.Name).ToList();
        if ((protect & Guard) != 0)
            parts.Add("PAGE_GUARD");
        if ((protect & 0x200) != 0)
            parts.Add("PAGE_NOCACHE");
        if ((protect & 0x400) != 0)
            parts.Add("PAGE_WRITECOMBINE");
        return parts.Count == 0 ? $"0x{protect:x}" : string.Join("|", parts);
    }
}
