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
    DumpKernelInfo? Kernel,
    IReadOnlyList<string> Hints);

/// <param name="Format">"user-minidump" or "kernel-dump"</param>
/// <param name="Sha256">Hex SHA-256 of the file; null when the file is larger than the hashing limit</param>
/// <param name="FullMemory">True when the dump contains the full process memory</param>
/// <param name="CreatedUtc">When the dump was written (minidump header timestamp)</param>
public record DumpFileInfo(
    string Path,
    long SizeBytes,
    string Format,
    string? Sha256,
    bool FullMemory,
    DateTimeOffset? CreatedUtc);

/// <param name="Architecture">"x64", "x86", "arm64", ...</param>
/// <param name="OsVersion">Windows version as major.minor.build</param>
/// <param name="ProductType">"workstation", "server" or "domain-controller"</param>
public record DumpSystemInfo(
    string Architecture,
    int ProcessorCount,
    string OsVersion,
    string? ProductType);

/// <param name="Bitness">32 or 64, from the dump's processor architecture</param>
/// <param name="Uptime">How long the process had been running when the dump was written</param>
public record DumpProcessInfo(
    string? ImageName,
    string? ImagePath,
    uint? ProcessId,
    int? Bitness,
    DateTimeOffset? StartedUtc,
    TimeSpan? Uptime);

/// <param name="Code">Exception code, e.g. "0xC0000005"</param>
/// <param name="CodeName">Symbolic name, e.g. "EXCEPTION_ACCESS_VIOLATION"; "unknown" when not recognised</param>
/// <param name="Address">Address of the faulting instruction</param>
/// <param name="Module">Module that contains the faulting instruction; null when it is outside every module</param>
/// <param name="ModuleOffset">Offset of the faulting instruction inside <paramref name="Module"/></param>
/// <param name="AccessType">For access violations: "read", "write" or "execute"</param>
/// <param name="TargetAddress">For access violations: the address that was accessed</param>
public record DumpFaultInfo(
    string Code,
    string CodeName,
    uint ThreadId,
    string Address,
    string? Module,
    string? ModuleOffset,
    string? AccessType,
    string? TargetAddress,
    IReadOnlyList<string> Parameters);

public record DumpThreadSummary(int Count, uint? FaultingThreadId);

/// <param name="Notable">The faulting module plus every module that is not part of Windows</param>
public record DumpModuleSummary(
    int Count,
    int WindowsModuleCount,
    IReadOnlyList<DumpModuleInfo> Notable);

/// <param name="SymbolKey">The key a symbol server files the PDB under: GUID (no dashes) followed by the age in hex</param>
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
    bool IsFaulting);

/// <param name="Kind">".NET" (CoreCLR) or ".NET Framework"</param>
public record DumpRuntimeInfo(string Kind, string? Version);

public record DumpKernelInfo(
    string BugCheckCode,
    string? BugCheckName,
    IReadOnlyList<string> Parameters,
    string Architecture,
    int ProcessorCount);

public record TriageDumpRequest(
    [Required(ErrorMessage = "Dump file path is required")]
    [property: JsonPropertyName("dumpFilePath")]
    string DumpFilePath);
