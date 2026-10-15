using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Shared.Models;

/// <summary>
/// Result of triage_dump: a first look at a dump read straight from its minidump streams,
/// without starting the debugger or loading symbols.
/// </summary>
public record DumpTriageResult(
    DumpFileInfo Dump,
    DumpSystemInfo? System,
    DumpProcessInfo? Process,
    DumpFaultInfo? Fault,
    DumpThreadSummary? Threads,
    DumpModuleSummary? Modules,
    DumpRuntimeInfo? Runtime,
    DumpMemoryInfo? Memory,
    DumpHandleSummary? Handles,
    DumpSymbolStatus? Symbols,
    DumpKernelInfo? Kernel,
    IReadOnlyList<string> Hints);

/// <param name="Format">"user-minidump" or "kernel-dump"</param>
/// <param name="Sha256">Hex SHA-256 of the file; null when the file is larger than the hashing limit</param>
/// <param name="FullMemory">True when the dump contains the full process memory</param>
/// <param name="CreatedUtc">When the dump was written (minidump header timestamp)</param>
/// <param name="Contents">What the dump carries, e.g. "full-memory", "handle-data", "thread-info", "memory-info", "unloaded-modules", "thread-names"</param>
/// <param name="Comment">Comment stream written by the dumper (ProcDump, WER, .dump /c)</param>
public record DumpFileInfo(
    string Path,
    long SizeBytes,
    string Format,
    string? Sha256,
    bool FullMemory,
    DateTimeOffset? CreatedUtc,
    IReadOnlyList<string> Contents,
    string? Comment);

/// <param name="Architecture">"x64", "x86", "arm64", ...</param>
/// <param name="OsVersion">Windows version as major.minor.build</param>
/// <param name="ProductType">"workstation", "server" or "domain-controller"</param>
public record DumpSystemInfo(
    string Architecture,
    int ProcessorCount,
    string OsVersion,
    string? ProductType);

/// <param name="Bitness">32 or 64; 32 for a WoW64 process even when the dump is 64-bit</param>
/// <param name="Uptime">How long the process had been running when the dump was written</param>
/// <param name="CpuTime">User plus kernel time of all threads</param>
/// <param name="Wow64">A 32-bit process captured by a 64-bit dumper</param>
/// <param name="DebugRuntime">A debug C runtime (ucrtbased.dll, vcruntime140d.dll, ...) is loaded: a debug build</param>
public record DumpProcessInfo(
    string? ImageName,
    string? ImagePath,
    uint? ProcessId,
    int? Bitness,
    DateTimeOffset? StartedUtc,
    TimeSpan? Uptime,
    TimeSpan? CpuTime,
    bool Wow64,
    bool DebugRuntime);

/// <param name="Code">Exception code, e.g. "0xC0000005"</param>
/// <param name="CodeName">Symbolic name, e.g. "EXCEPTION_ACCESS_VIOLATION"; "unknown" when not recognised</param>
/// <param name="Classification">Short stable class, e.g. "null-pointer", "use-after-free-pattern", "call-into-unloaded-module", "stack-overflow", "fail-fast"</param>
/// <param name="ThreadName">Name of the faulting thread, when the dump records thread names</param>
/// <param name="Address">Address of the faulting instruction</param>
/// <param name="Module">Module that contains the faulting instruction; null when it is outside every module</param>
/// <param name="ModuleOffset">Offset of the faulting instruction inside <paramref name="Module"/></param>
/// <param name="UnloadedModule">An unloaded module whose old range contains the faulting instruction or the accessed address</param>
/// <param name="AccessType">For access violations: "read", "write" or "execute"</param>
/// <param name="TargetAddress">For access violations: the address that was accessed</param>
/// <param name="TargetRegion">Memory region of <paramref name="TargetAddress"/>, when the dump has memory info</param>
/// <param name="FastFailCode">For fail-fast exceptions: the FAST_FAIL_* code, e.g. "FAST_FAIL_STACK_COOKIE_CHECK_FAILURE"</param>
/// <param name="Registers">General-purpose registers of the faulting thread (x64 and x86)</param>
/// <param name="SuspiciousValues">Registers or addresses holding a known fill pattern (freed or uninitialised memory)</param>
/// <param name="CppException">Thrown C++ type and message, read from captured memory</param>
/// <param name="DelayLoad">DLL and function a delay-load failure was about</param>
public record DumpFaultInfo(
    string Code,
    string CodeName,
    string Classification,
    uint ThreadId,
    string? ThreadName,
    string Address,
    string? Module,
    string? ModuleOffset,
    string? UnloadedModule,
    string? AccessType,
    string? TargetAddress,
    DumpMemoryRegion? TargetRegion,
    string? FastFailCode,
    IReadOnlyList<string> Parameters,
    IReadOnlyDictionary<string, string> Registers,
    IReadOnlyList<DumpSuspiciousValue> SuspiciousValues,
    DumpCppExceptionInfo? CppException,
    DumpDelayLoadInfo? DelayLoad);

/// <param name="State">"commit", "reserve" or "free"</param>
/// <param name="Protect">e.g. "PAGE_READWRITE", "PAGE_EXECUTE_READ", "PAGE_NOACCESS|PAGE_GUARD"</param>
/// <param name="Type">"image", "mapped" or "private"</param>
public record DumpMemoryRegion(
    string BaseAddress,
    string Size,
    string State,
    string? Protect,
    string? Type);

/// <param name="Location">Register name ("rcx") or "target" for the accessed address</param>
/// <param name="Meaning">e.g. "freed memory (debug CRT 0xDD fill)"</param>
public record DumpSuspiciousValue(string Location, string Value, string Meaning);

/// <param name="Type">Most derived thrown type, e.g. "std::out_of_range"</param>
/// <param name="CatchableTypes">Every type a catch clause could match, most derived first</param>
/// <param name="Message">std::exception::what(), when the type derives from std::exception</param>
public record DumpCppExceptionInfo(string Type, IReadOnlyList<string> CatchableTypes, string? Message);

/// <param name="LastError">Win32 error of the failed LoadLibrary/GetProcAddress</param>
public record DumpDelayLoadInfo(string Dll, string? Procedure, uint? LastError);

/// <param name="Exited">Threads that had already exited when the dump was written</param>
/// <param name="Busiest">Threads with the most CPU time, busiest first (needs thread info in the dump)</param>
/// <param name="Named">Threads with a name, when the dump records thread names</param>
public record DumpThreadSummary(
    int Count,
    uint? FaultingThreadId,
    int Exited,
    IReadOnlyList<DumpThreadInfo> Busiest,
    IReadOnlyList<DumpNamedThread> Named);

/// <param name="CpuShare">CPU time divided by the thread's age: near 1 means it ran flat out</param>
/// <param name="StartModule">Module containing the thread's start address</param>
public record DumpThreadInfo(
    uint Id,
    string? Name,
    TimeSpan CpuTime,
    double? CpuShare,
    string StartAddress,
    string? StartModule);

public record DumpNamedThread(uint Id, string Name);

/// <param name="Notable">The faulting module plus every module that is not part of Windows</param>
/// <param name="Unloaded">Recently unloaded modules (Windows keeps the last few)</param>
public record DumpModuleSummary(
    int Count,
    int WindowsModuleCount,
    IReadOnlyList<DumpModuleInfo> Notable,
    IReadOnlyList<DumpUnloadedModuleInfo> Unloaded);

/// <param name="SymbolKey">The key a symbol server files the PDB under: GUID (no dashes) followed by the age in hex</param>
/// <param name="SymbolsCached">Whether the PDB is already in the symbol cache; null when not checked</param>
public record DumpModuleInfo(
    string Name,
    string Path,
    string BaseAddress,
    long Size,
    string? FileVersion,
    DateTimeOffset? Timestamp,
    string? PdbName,
    string? PdbGuid,
    int? PdbAge,
    string? SymbolKey,
    bool IsFaulting,
    bool? SymbolsCached = null);

public record DumpUnloadedModuleInfo(string Name, string BaseAddress, long Size, DateTimeOffset? Timestamp);

/// <param name="Kind">".NET" (CoreCLR) or ".NET Framework"</param>
public record DumpRuntimeInfo(string Kind, string? Version);

/// <param name="PrivateBytes">Commit charge of the process</param>
/// <param name="JobCommitLimit">Private commit limit of the job object the process runs in</param>
/// <param name="AddressSpaceUsed">Reserved plus committed user address space, from the memory regions</param>
/// <param name="LargestFreeRegion">Largest free block of the user address space (fragmentation on 32-bit)</param>
/// <param name="SystemCommitUsed">System-wide commit charge</param>
/// <param name="SystemCommitLimit">System-wide commit limit (RAM plus page files)</param>
public record DumpMemoryInfo(
    long? PrivateBytes,
    long? PeakPrivateBytes,
    long? WorkingSet,
    long? PeakWorkingSet,
    long? VirtualSize,
    long? JobCommitLimit,
    long? AddressSpaceUsed,
    long? LargestFreeRegion,
    long? SystemCommitUsed,
    long? SystemCommitLimit,
    long? SystemPhysicalMemory);

/// <param name="ByType">Open handles per object type, most first</param>
public record DumpHandleSummary(int Count, IReadOnlyList<DumpHandleTypeCount> ByType);

public record DumpHandleTypeCount(string Type, int Count);

/// <param name="CachePath">Symbol cache that was checked (the one load_dump uses)</param>
/// <param name="Checked">Modules with a PDB identity</param>
/// <param name="Cached">Of those, PDBs already in the cache</param>
/// <param name="Missing">Non-Windows modules whose PDB is not in the cache</param>
/// <param name="WindowsMissing">Windows modules whose PDB is not in the cache (downloaded from the Microsoft symbol server)</param>
public record DumpSymbolStatus(
    string CachePath,
    int Checked,
    int Cached,
    IReadOnlyList<string> Missing,
    int WindowsMissing);

/// <param name="Summary">The bug check parameters decoded into a sentence, for the common bug checks</param>
/// <param name="DumpType">"complete", "kernel", "small", "complete (bitmap)", "kernel (bitmap)"</param>
public record DumpKernelInfo(
    string BugCheckCode,
    string? BugCheckName,
    IReadOnlyList<string> Parameters,
    string? Summary,
    string Architecture,
    int ProcessorCount,
    string? DumpType,
    DateTimeOffset? SystemTimeUtc,
    TimeSpan? SystemUptime);

/// <param name="SymbolCache">Symbol cache to check for the dump's PDBs; defaults to the server's cache</param>
public record TriageDumpRequest(
    [Required(ErrorMessage = "Dump file path is required")]
    [property: JsonPropertyName("dumpFilePath")]
    string DumpFilePath,

    [property: JsonPropertyName("symbolCache")]
    string? SymbolCache = null);
