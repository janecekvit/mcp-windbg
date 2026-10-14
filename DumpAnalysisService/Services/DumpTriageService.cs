using System.Security.Cryptography;
using DumpAnalysisService.Infrastructure.Minidump;
using DumpAnalysisService.Security;
using Microsoft.Extensions.Options;
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

    private readonly ILogger<DumpTriageService> _logger;
    private readonly SecurityOptions _securityOptions;

    public DumpTriageService(ILogger<DumpTriageService> logger, IOptions<SecurityOptions> securityOptions)
    {
        _logger = logger;
        _securityOptions = securityOptions.Value;
    }

    public async Task<DumpTriageResult> TriageAsync(string dumpFilePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(dumpFilePath))
            throw new FileNotFoundException($"Dump file not found: {dumpFilePath}", dumpFilePath);

        DumpPathPolicy.EnsureAllowed(dumpFilePath, _securityOptions);

        await using var stream = new FileStream(dumpFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
        var data = MinidumpParser.Parse(stream);

        string? sha256 = null;
        if (stream.Length <= MaxHashedFileSize)
        {
            stream.Position = 0;
            sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
        }

        var result = Build(dumpFilePath, stream.Length, sha256, data);
        _logger.LogInformation("Triaged {DumpFile}: {Format}, fault {Code} in {Module}",
            dumpFilePath, result.Dump.Format, result.Fault?.Code ?? "none", result.Fault?.Module ?? "-");
        return result;
    }

    /// <summary>Pure mapping from parsed streams to the triage result (unit-testable without files).</summary>
    public static DumpTriageResult Build(string path, long sizeBytes, string? sha256, MinidumpData data)
    {
        if (data.Kernel is { } kernel)
            return _BuildKernel(path, sizeBytes, sha256, kernel);

        var architecture = data.SystemInfo is { } sys ? _ArchitectureName(sys.ProcessorArchitecture) : null;
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
            Bitness: architecture switch { "x86" or "arm" => 32, "x64" or "arm64" or "ia64" => 64, _ => null },
            StartedUtc: startedUtc,
            Uptime: startedUtc is { } start && data.TimeDateStamp is { } written && written >= start ? written - start : null);

        var fault = data.Exception is { } e ? _BuildFault(e, faultingModule) : null;

        var windowsModules = data.Modules.Count(m => _IsWindowsModule(m.Path));
        var notable = data.Modules
            .Where(m => ReferenceEquals(m, faultingModule) || !_IsWindowsModule(m.Path))
            .Take(50)
            .Select(m => _ToModuleInfo(m, ReferenceEquals(m, faultingModule)))
            .ToList();

        var runtime = _DetectRuntime(data.Modules);

        var result = new DumpTriageResult(
            Dump: new DumpFileInfo(path, sizeBytes, "user-minidump", sha256, data.HasFullMemory, data.TimeDateStamp),
            System: system,
            Process: process,
            Fault: fault,
            Threads: new DumpThreadSummary(data.ThreadIds.Count, data.Exception?.ThreadId),
            Modules: new DumpModuleSummary(data.Modules.Count, windowsModules, notable),
            Runtime: runtime,
            Kernel: null,
            Hints: []);

        return result with { Hints = _BuildHints(result, data, faultingModule) };
    }

    private static DumpFaultInfo _BuildFault(MinidumpException exception, MinidumpModule? module)
    {
        string? accessType = null;
        string? target = null;
        if (exception.Code is ExceptionCodes.AccessViolation or ExceptionCodes.InPageError && exception.Parameters.Count >= 2)
        {
            accessType = exception.Parameters[0] switch { 0 => "read", 1 => "write", 8 => "execute", _ => null };
            target = _Hex(exception.Parameters[1]);
        }

        return new DumpFaultInfo(
            Code: $"0x{exception.Code:X8}",
            CodeName: ExceptionCodes.NameOf(exception.Code),
            ThreadId: exception.ThreadId,
            Address: _Hex(exception.Address),
            Module: module is null ? null : _ModuleName(module.Path),
            ModuleOffset: module is null ? null : _Hex(exception.Address - module.BaseAddress),
            AccessType: accessType,
            TargetAddress: target,
            Parameters: exception.Parameters.Select(_Hex).ToList());
    }

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

    private static List<string> _BuildHints(DumpTriageResult result, MinidumpData data, MinidumpModule? faultingModule)
    {
        var hints = new List<string>();
        var fault = result.Fault;

        if (fault is null)
        {
            hints.Add("No exception record: this is a snapshot (hang or on-demand dump). Look at the thread stacks with predefined_analysis \"threads\" and at locks with \"locks\".");
        }
        else
        {
            var where = fault.Module is null ? $"at {fault.Address}" : $"in {fault.Module}+{fault.ModuleOffset}";
            switch (data.Exception!.Code)
            {
                case ExceptionCodes.AccessViolation when fault.TargetAddress is not null && Convert.ToUInt64(fault.TargetAddress, 16) < 0x10000:
                    hints.Add($"Null-pointer {fault.AccessType ?? "access"} (target {fault.TargetAddress}) {where}. Run basic_analysis for the faulting stack.");
                    break;
                case ExceptionCodes.AccessViolation:
                    hints.Add($"Access violation ({fault.AccessType ?? "access"} of {fault.TargetAddress ?? "unknown address"}) {where}. A wild or freed pointer is likely; run basic_analysis.");
                    break;
                case ExceptionCodes.StackOverflow:
                    hints.Add($"Stack overflow {where}. Look for a repeating frame (recursion) in the faulting thread's stack.");
                    break;
                case ExceptionCodes.ClrException:
                    hints.Add(".NET exception. Load the dump and use SOS (!pe, !clrstack) to see the managed exception and stack.");
                    break;
                case ExceptionCodes.CppException:
                    hints.Add("Unhandled C++ exception (throw). The throwing frame is below KERNELBASE!RaiseException on the faulting stack.");
                    break;
                case ExceptionCodes.FailFast or ExceptionCodes.FailFastException:
                    hints.Add($"Fail-fast {where}: the process terminated itself (security check, Environment.FailFast or __fastfail). Check the caller of the fail-fast.");
                    break;
                case ExceptionCodes.HeapCorruption:
                    hints.Add("Heap corruption detected by the heap manager. Enable page heap (gflags /p /enable) and reproduce for the real culprit.");
                    break;
                case ExceptionCodes.Breakpoint:
                    hints.Add("Break instruction: the dump was probably taken by a debugger or a hang monitor. Treat it as a snapshot and inspect all threads.");
                    break;
                default:
                    hints.Add($"{fault.CodeName} ({fault.Code}) {where}. Run basic_analysis for the faulting stack.");
                    break;
            }

            if (fault.Module is null && data.Exception.Code != ExceptionCodes.ClrException)
                hints.Add("The faulting address is outside every loaded module: JIT-compiled code, a corrupted return address or an unloaded module.");
        }

        if (faultingModule?.CodeView is { } cv)
            hints.Add($"Symbols for {_ModuleName(faultingModule.Path)}: {_FileName(cv.PdbPath)} (key {_SymbolKey(cv)}) must be on the symbol path for named frames.");

        if (!data.HasFullMemory)
            hints.Add("Mini dump without full memory: heap and object inspection will be limited.");

        return hints;
    }

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

        return new DumpTriageResult(
            Dump: new DumpFileInfo(path, sizeBytes, "kernel-dump", sha256, FullMemory: false, CreatedUtc: null),
            System: null,
            Process: null,
            Fault: null,
            Threads: null,
            Modules: null,
            Runtime: null,
            Kernel: new DumpKernelInfo(code, name, kernel.Parameters.Select(_Hex).ToList(), architecture, (int)kernel.NumberProcessors),
            Hints:
            [
                $"Kernel dump, bug check {code}{(name is null ? "" : $" ({name})")}. Triage reads only the kernel dump header; load it with a kernel debugger (kd) and run !analyze -v.",
            ]);
    }

    private static bool _IsWindowsModule(string path) =>
        path.Replace('/', '\\').Contains("\\Windows\\", StringComparison.OrdinalIgnoreCase);

    private static string _ModuleName(string path) => _FileName(path);

    /// <summary>The directory name a symbol server files the PDB under: GUID without dashes, then the age in hex.</summary>
    private static string _SymbolKey(MinidumpCodeView cv) => $"{cv.Guid:N}".ToUpperInvariant() + $"{cv.Age:X}";

    /// <summary>File name of a Windows path; works the same when the service runs on another OS.</summary>
    private static string _FileName(string path) => path[(path.LastIndexOfAny(['\\', '/']) + 1)..];

    private static string _ArchitectureName(ushort architecture) => architecture switch
    {
        0 => "x86",
        5 => "arm",
        6 => "ia64",
        9 => "x64",
        12 => "arm64",
        _ => $"unknown ({architecture})",
    };

    private static string _Hex(ulong value) => $"0x{value:x}";
}
