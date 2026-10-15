namespace DumpAnalysisService.Infrastructure.Minidump;

/// <summary>
/// Windows exception codes that commonly end a process, with their symbolic names.
/// </summary>
public static class ExceptionCodes
{
    public const uint AccessViolation = 0xC0000005;
    public const uint InPageError = 0xC0000006;
    public const uint InvalidHandle = 0xC0000008;
    public const uint NoMemory = 0xC0000017;
    public const uint IllegalInstruction = 0xC000001D;
    public const uint NoncontinuableException = 0xC0000025;
    public const uint InvalidDisposition = 0xC0000026;
    public const uint BadStack = 0xC0000028;
    public const uint IntegerDivideByZero = 0xC0000094;
    public const uint PrivilegedInstruction = 0xC0000096;
    public const uint StackOverflow = 0xC00000FD;
    public const uint DllNotFound = 0xC0000135;
    public const uint EntryPointNotFound = 0xC0000139;
    public const uint ControlCExit = 0xC000013A;
    public const uint DllInitFailed = 0xC0000142;
    public const uint PossibleDeadlock = 0xC0000194;
    public const uint StowedException = 0xC000027B;
    public const uint HeapCorruption = 0xC0000374;
    public const uint FailFast = 0xC0000409;
    public const uint InvalidCRuntimeParameter = 0xC0000417;
    public const uint FatalUserCallbackException = 0xC000041D;
    public const uint AssertionFailure = 0xC0000420;
    public const uint FailFastException = 0xC0000602;
    public const uint FatalAppExit = 0x40000015;
    public const uint Wow64SingleStep = 0x4000001E;
    public const uint Wow64Breakpoint = 0x4000001F;
    public const uint SetThreadName = 0x406D1388;
    public const uint DatatypeMisalignment = 0x80000002;
    public const uint Breakpoint = 0x80000003;
    public const uint DelayLoadModuleNotFound = 0xC06D007E;
    public const uint DelayLoadProcNotFound = 0xC06D007F;
    public const uint ClrException = 0xE0434352;
    public const uint CppException = 0xE06D7363;

    private static readonly Dictionary<uint, string> _names = new()
    {
        [AccessViolation] = "EXCEPTION_ACCESS_VIOLATION",
        [InPageError] = "EXCEPTION_IN_PAGE_ERROR",
        [InvalidHandle] = "EXCEPTION_INVALID_HANDLE",
        [NoMemory] = "STATUS_NO_MEMORY",
        [IllegalInstruction] = "EXCEPTION_ILLEGAL_INSTRUCTION",
        [NoncontinuableException] = "EXCEPTION_NONCONTINUABLE_EXCEPTION",
        [InvalidDisposition] = "EXCEPTION_INVALID_DISPOSITION",
        [BadStack] = "STATUS_BAD_STACK",
        [0xC000008C] = "EXCEPTION_ARRAY_BOUNDS_EXCEEDED",
        [0xC000008D] = "EXCEPTION_FLT_DENORMAL_OPERAND",
        [0xC000008E] = "EXCEPTION_FLT_DIVIDE_BY_ZERO",
        [0xC000008F] = "EXCEPTION_FLT_INEXACT_RESULT",
        [0xC0000090] = "EXCEPTION_FLT_INVALID_OPERATION",
        [0xC0000091] = "EXCEPTION_FLT_OVERFLOW",
        [0xC0000092] = "EXCEPTION_FLT_STACK_CHECK",
        [0xC0000093] = "EXCEPTION_FLT_UNDERFLOW",
        [IntegerDivideByZero] = "EXCEPTION_INT_DIVIDE_BY_ZERO",
        [0xC0000095] = "EXCEPTION_INT_OVERFLOW",
        [PrivilegedInstruction] = "EXCEPTION_PRIV_INSTRUCTION",
        [StackOverflow] = "EXCEPTION_STACK_OVERFLOW",
        [DllNotFound] = "STATUS_DLL_NOT_FOUND",
        [EntryPointNotFound] = "STATUS_ENTRYPOINT_NOT_FOUND",
        [ControlCExit] = "STATUS_CONTROL_C_EXIT",
        [DllInitFailed] = "STATUS_DLL_INIT_FAILED",
        [PossibleDeadlock] = "STATUS_POSSIBLE_DEADLOCK",
        [StowedException] = "STATUS_STOWED_EXCEPTION",
        [0xC00002B4] = "STATUS_FLOAT_MULTIPLE_FAULTS",
        [0xC00002B5] = "STATUS_FLOAT_MULTIPLE_TRAPS",
        [HeapCorruption] = "STATUS_HEAP_CORRUPTION",
        [FailFast] = "STATUS_STACK_BUFFER_OVERRUN (fail-fast)",
        [InvalidCRuntimeParameter] = "STATUS_INVALID_CRUNTIME_PARAMETER",
        [FatalUserCallbackException] = "STATUS_FATAL_USER_CALLBACK_EXCEPTION",
        [AssertionFailure] = "STATUS_ASSERTION_FAILURE",
        [FailFastException] = "STATUS_FAIL_FAST_EXCEPTION",
        [FatalAppExit] = "STATUS_FATAL_APP_EXIT",
        [Wow64SingleStep] = "STATUS_WX86_SINGLE_STEP",
        [Wow64Breakpoint] = "STATUS_WX86_BREAKPOINT",
        [SetThreadName] = "MS_VC_EXCEPTION (SetThreadName)",
        [0x80000001] = "STATUS_GUARD_PAGE_VIOLATION",
        [DatatypeMisalignment] = "EXCEPTION_DATATYPE_MISALIGNMENT",
        [Breakpoint] = "EXCEPTION_BREAKPOINT",
        [0x80000004] = "EXCEPTION_SINGLE_STEP",
        [DelayLoadModuleNotFound] = "DELAYLOAD_MODULE_NOT_FOUND",
        [DelayLoadProcNotFound] = "DELAYLOAD_PROC_NOT_FOUND",
        [ClrException] = "CLR_EXCEPTION (.NET)",
        [0xE0434F4D] = "CLR_EXCEPTION (.NET Framework 1.x/2.0)",
        [CppException] = "CPP_EH_EXCEPTION (C++ throw)",
    };

    public static string NameOf(uint code) => _names.TryGetValue(code, out var name) ? name : "unknown";

    /// <summary>
    /// A short, stable class for the fault, independent of the hint wording (also a good grouping key).
    /// Access violations are refined by the caller from the access details.
    /// </summary>
    public static string ClassOf(uint code) => code switch
    {
        AccessViolation => "access-violation",
        InPageError => "in-page-error",
        InvalidHandle => "invalid-handle",
        NoMemory => "out-of-memory",
        IllegalInstruction or PrivilegedInstruction => "illegal-instruction",
        IntegerDivideByZero or 0xC0000095 => "integer-arithmetic",
        >= 0xC000008D and <= 0xC0000093 or 0xC00002B4 or 0xC00002B5 => "floating-point",
        StackOverflow => "stack-overflow",
        DllNotFound or EntryPointNotFound or DllInitFailed or 0xC000007B => "dll-load-failure",
        DelayLoadModuleNotFound or DelayLoadProcNotFound => "delay-load-failure",
        HeapCorruption => "heap-corruption",
        FailFast or FailFastException => "fail-fast",
        InvalidCRuntimeParameter => "invalid-crt-parameter",
        FatalAppExit => "abort",
        AssertionFailure => "assertion",
        PossibleDeadlock => "possible-deadlock",
        FatalUserCallbackException => "user-callback-exception",
        StowedException => "stowed-exception",
        NoncontinuableException or InvalidDisposition or BadStack => "exception-chain-corruption",
        ControlCExit => "control-c",
        Breakpoint or Wow64Breakpoint or 0x80000004 or Wow64SingleStep => "breakpoint",
        SetThreadName => "thread-naming",
        ClrException or 0xE0434F4D => "dotnet-exception",
        CppException => "cpp-exception",
        _ when (code & 0xF0000000) == 0xE0000000 => "application-exception",
        _ => "unknown",
    };
}

/// <summary>
/// FAST_FAIL_* codes (winnt.h) carried in Parameters[0] of a fail-fast exception (0xC0000409)
/// and in parameter 1 of bug check 0x139.
/// </summary>
public static class FastFailCodes
{
    public const uint StackCookieCheckFailure = 2;
    public const uint FatalAppExit = 7;

    private static readonly Dictionary<uint, string> _names = new()
    {
        [0] = "FAST_FAIL_LEGACY_GS_VIOLATION",
        [1] = "FAST_FAIL_VTGUARD_CHECK_FAILURE",
        [2] = "FAST_FAIL_STACK_COOKIE_CHECK_FAILURE",
        [3] = "FAST_FAIL_CORRUPT_LIST_ENTRY",
        [4] = "FAST_FAIL_INCORRECT_STACK",
        [5] = "FAST_FAIL_INVALID_ARG",
        [6] = "FAST_FAIL_GS_COOKIE_INIT",
        [7] = "FAST_FAIL_FATAL_APP_EXIT",
        [8] = "FAST_FAIL_RANGE_CHECK_FAILURE",
        [9] = "FAST_FAIL_UNSAFE_REGISTRY_ACCESS",
        [10] = "FAST_FAIL_GUARD_ICALL_CHECK_FAILURE",
        [11] = "FAST_FAIL_GUARD_WRITE_CHECK_FAILURE",
        [12] = "FAST_FAIL_INVALID_FIBER_SWITCH",
        [13] = "FAST_FAIL_INVALID_SET_OF_CONTEXT",
        [14] = "FAST_FAIL_INVALID_REFERENCE_COUNT",
        [18] = "FAST_FAIL_INVALID_JUMP_BUFFER",
        [19] = "FAST_FAIL_MRDATA_MODIFIED",
        [20] = "FAST_FAIL_CERTIFICATION_FAILURE",
        [21] = "FAST_FAIL_INVALID_EXCEPTION_CHAIN",
        [22] = "FAST_FAIL_CRYPTO_LIBRARY",
        [23] = "FAST_FAIL_INVALID_CALL_IN_DLL_CALLOUT",
        [24] = "FAST_FAIL_INVALID_IMAGE_BASE",
        [25] = "FAST_FAIL_DLOAD_PROTECTION_FAILURE",
        [26] = "FAST_FAIL_UNSAFE_EXTENSION_CALL",
        [27] = "FAST_FAIL_DEPRECATED_SERVICE_INVOKED",
        [28] = "FAST_FAIL_INVALID_BUFFER_ACCESS",
        [29] = "FAST_FAIL_INVALID_BALANCED_TREE",
        [30] = "FAST_FAIL_INVALID_NEXT_THREAD",
        [31] = "FAST_FAIL_GUARD_ICALL_CHECK_SUPPRESSED",
        [32] = "FAST_FAIL_APCS_DISABLED",
        [33] = "FAST_FAIL_INVALID_IDLE_STATE",
        [34] = "FAST_FAIL_MRDATA_PROTECTION_FAILURE",
        [35] = "FAST_FAIL_UNEXPECTED_HEAP_EXCEPTION",
        [36] = "FAST_FAIL_INVALID_LOCK_STATE",
        [37] = "FAST_FAIL_GUARD_JUMPTABLE",
        [38] = "FAST_FAIL_INVALID_LONGJUMP_TARGET",
        [39] = "FAST_FAIL_INVALID_DISPATCH_CONTEXT",
        [40] = "FAST_FAIL_INVALID_THREAD",
        [41] = "FAST_FAIL_INVALID_SYSCALL_NUMBER",
        [42] = "FAST_FAIL_INVALID_FILE_OPERATION",
        [43] = "FAST_FAIL_LPAC_ACCESS_DENIED",
        [44] = "FAST_FAIL_GUARD_SS_FAILURE",
        [45] = "FAST_FAIL_LOADER_CONTINUITY_FAILURE",
        [46] = "FAST_FAIL_GUARD_EXPORT_SUPPRESSION_FAILURE",
        [47] = "FAST_FAIL_INVALID_CONTROL_STACK",
        [48] = "FAST_FAIL_SET_CONTEXT_DENIED",
        [49] = "FAST_FAIL_INVALID_IAT",
        [50] = "FAST_FAIL_HEAP_METADATA_CORRUPTION",
        [51] = "FAST_FAIL_PAYLOAD_RESTRICTION_VIOLATION",
        [52] = "FAST_FAIL_LOW_LABEL_ACCESS_DENIED",
        [53] = "FAST_FAIL_ENCLAVE_CALL_FAILURE",
        [54] = "FAST_FAIL_UNHANDLED_LSS_EXCEPTON",
        [55] = "FAST_FAIL_ADMINLESS_ACCESS_DENIED",
        [56] = "FAST_FAIL_UNEXPECTED_CALL",
        [57] = "FAST_FAIL_CONTROL_INVALID_RETURN_ADDRESS",
        [58] = "FAST_FAIL_UNEXPECTED_HOST_BEHAVIOR",
        [59] = "FAST_FAIL_FLAGS_CORRUPTION",
        [60] = "FAST_FAIL_VEH_CORRUPTION",
        [61] = "FAST_FAIL_ETW_CORRUPTION",
        [62] = "FAST_FAIL_RIO_ABORT",
        [63] = "FAST_FAIL_INVALID_PFN",
        [64] = "FAST_FAIL_GUARD_ICALL_CHECK_FAILURE_XFG",
        [65] = "FAST_FAIL_CAST_GUARD",
        [66] = "FAST_FAIL_HOST_VISIBILITY_CHANGE",
        [67] = "FAST_FAIL_KERNEL_CET_SHADOW_STACK_ASSIST",
        [68] = "FAST_FAIL_PATCH_CALLBACK_FAILED",
        [69] = "FAST_FAIL_NTDLL_PATCH_FAILED",
        [70] = "FAST_FAIL_INVALID_FLS_DATA",
    };

    public static string? NameOf(ulong code) => code <= uint.MaxValue ? _names.GetValueOrDefault((uint)code) : null;

    /// <summary>What a fail-fast code usually means, for the hint; null when there is nothing more to say than the name.</summary>
    public static string? Meaning(ulong code) => code switch
    {
        0 or 2 or 6 => "the /GS stack cookie was overwritten: a local buffer overran (stack corruption)",
        1 => "a VTGuard check failed: a C++ object's vtable pointer is corrupt",
        3 or 29 or 35 or 50 => "a heap or list structure is corrupt (often a double free, use after free or heap overrun)",
        5 => "an invalid argument reached a function that terminates on bad input (e.g. the CRT invalid parameter handler)",
        7 => "the process terminated itself on purpose: abort(), std::terminate, Environment.FailFast or a runtime's fatal error",
        8 => "a range check failed (index out of bounds detected by the code)",
        10 or 64 => "Control Flow Guard blocked an indirect call to an invalid target (corrupt function pointer or vtable)",
        11 => "a CFG write check failed",
        21 => "the SEH exception handler chain is corrupt (SEHOP)",
        38 => "longjmp to an invalid target",
        44 or 57 => "the CET shadow stack disagrees with the return address: the stack was corrupted",
        _ => null,
    };
}

/// <summary>
/// Common kernel bug check codes and what their parameters mean.
/// </summary>
public static class BugCheckCodes
{
    private static readonly Dictionary<uint, string> _names = new()
    {
        [0x0A] = "IRQL_NOT_LESS_OR_EQUAL",
        [0x19] = "BAD_POOL_HEADER",
        [0x1A] = "MEMORY_MANAGEMENT",
        [0x1E] = "KMODE_EXCEPTION_NOT_HANDLED",
        [0x3B] = "SYSTEM_SERVICE_EXCEPTION",
        [0x50] = "PAGE_FAULT_IN_NONPAGED_AREA",
        [0x77] = "KERNEL_STACK_INPAGE_ERROR",
        [0x7A] = "KERNEL_DATA_INPAGE_ERROR",
        [0x7E] = "SYSTEM_THREAD_EXCEPTION_NOT_HANDLED",
        [0x7F] = "UNEXPECTED_KERNEL_MODE_TRAP",
        [0x8E] = "KERNEL_MODE_EXCEPTION_NOT_HANDLED",
        [0x9C] = "MACHINE_CHECK_EXCEPTION",
        [0x9F] = "DRIVER_POWER_STATE_FAILURE",
        [0x101] = "CLOCK_WATCHDOG_TIMEOUT",
        [0x109] = "CRITICAL_STRUCTURE_CORRUPTION",
        [0x116] = "VIDEO_TDR_FAILURE",
        [0x117] = "VIDEO_TDR_TIMEOUT_DETECTED",
        [0x124] = "WHEA_UNCORRECTABLE_ERROR",
        [0x133] = "DPC_WATCHDOG_VIOLATION",
        [0x139] = "KERNEL_SECURITY_CHECK_FAILURE",
        [0x141] = "VIDEO_ENGINE_TIMEOUT_DETECTED",
        [0xBE] = "ATTEMPTED_WRITE_TO_READONLY_MEMORY",
        [0xC1] = "SPECIAL_POOL_DETECTED_MEMORY_CORRUPTION",
        [0xC2] = "BAD_POOL_CALLER",
        [0xC4] = "DRIVER_VERIFIER_DETECTED_VIOLATION",
        [0xC5] = "DRIVER_CORRUPTED_EXPOOL",
        [0xCE] = "DRIVER_UNLOADED_WITHOUT_CANCELLING_PENDING_OPERATIONS",
        [0xD1] = "DRIVER_IRQL_NOT_LESS_OR_EQUAL",
        [0xD5] = "DRIVER_PAGE_FAULT_IN_FREED_SPECIAL_POOL",
        [0xE2] = "MANUALLY_INITIATED_CRASH",
        [0xEA] = "THREAD_STUCK_IN_DEVICE_DRIVER",
        [0xEF] = "CRITICAL_PROCESS_DIED",
        [0xF4] = "CRITICAL_OBJECT_TERMINATION",
        [0xFC] = "ATTEMPTED_EXECUTE_OF_NOEXECUTE_MEMORY",
        [0x1000007E] = "SYSTEM_THREAD_EXCEPTION_NOT_HANDLED_M",
        [0x1000008E] = "KERNEL_MODE_EXCEPTION_NOT_HANDLED_M",
    };

    public static string? NameOf(uint code) => _names.GetValueOrDefault(code);

    /// <summary>A sentence decoding the parameters of the most common bug checks; null for the rest.</summary>
    public static string? Describe(uint code, IReadOnlyList<ulong> p)
    {
        string Hex(ulong v) => $"0x{v:x}";
        string P(int i) => i < p.Count ? Hex(p[i]) : "?";
        ulong V(int i) => i < p.Count ? p[i] : 0;

        return code switch
        {
            0x0A or 0xD1 => $"{(code == 0xD1 ? "A driver" : "Kernel code")} touched {P(0)} at IRQL {V(1)} " +
                $"({_AccessOf(code == 0xD1 ? V(2) : V(2) & 0x9, code == 0xD1)}) from instruction {P(3)}: " +
                "usually a driver using an invalid or paged-out pointer at raised IRQL.",
            0x50 => $"Invalid {(V(1) switch { 0 => "read of", 1 => "write to", 2 or 10 => "execution at", _ => "access to" })} " +
                $"{P(0)} from instruction {P(2)}: a bad or freed pointer, or faulty RAM.",
            0x1E or 0x7E or 0x8E or 0x1000007E or 0x1000008E => $"Exception {(V(0) <= uint.MaxValue ? $"0x{V(0):X8} ({ExceptionCodes.NameOf((uint)V(0))})" : P(0))} " +
                $"not handled at {P(1)}.",
            0x3B => $"Exception 0x{V(0):X8} ({ExceptionCodes.NameOf((uint)V(0))}) in a system service at {P(1)}.",
            0x7F => $"Trap {V(0)}{(V(0) == 8 ? " (double fault: almost always a kernel stack overflow)" : V(0) == 0 ? " (divide error)" : "")}.",
            0x133 => V(0) == 0
                ? "A single DPC or ISR ran longer than its time allotment."
                : "The system spent too long cumulatively at DISPATCH_LEVEL or above (often a driver spinning or a storage stall).",
            0x139 => $"Kernel fail-fast {P(0)} ({FastFailCodes.NameOf(V(0)) ?? "unknown code"}): {FastFailCodes.Meaning(V(0)) ?? "a kernel integrity check failed"}.",
            0xEF => $"A critical {(V(1) == 0 ? "process" : "thread")} terminated (object {P(0)}).",
            0xF4 => $"A critical {(V(0) == 3 ? "process" : "thread")} (object {P(1)}) terminated.",
            0x9F => V(0) switch
            {
                3 => $"A device object ({P(1)}) held a power IRP ({P(3)}) too long.",
                4 => "A power transition timed out waiting to synchronise with the PnP subsystem.",
                _ => $"Power state failure, subtype {P(0)}.",
            },
            0x1A or 0xC2 or 0x19 => $"Subtype {P(0)}: look it up on the bug check reference page; pool and memory manager corruption is usually a driver bug.",
            0x124 or 0x9C => "The hardware reported an uncorrectable error (CPU, memory, bus): check WHEA logs and hardware before software.",
            0x101 => $"Processor {V(3)} stopped responding to the clock interrupt: hardware, firmware or a hypervisor problem.",
            0x116 or 0x117 or 0x141 => "The display driver stopped responding and could not be recovered (TDR).",
            0xE2 => "The crash was triggered on purpose (keyboard or NotMyFault): a hang dump; look at what the system was waiting on.",
            0xEA => $"A thread ({P(0)}) was stuck in a device driver loop.",
            0xFC => $"Attempt to execute non-executable memory at {P(0)}.",
            0xBE => $"Attempt to write to read-only memory at {P(0)}.",
            0x7A or 0x77 => $"A kernel page could not be read in (status 0x{V(1):X8}): disk, controller or paging file problem.",
            _ => null,
        };
    }

    private static string _AccessOf(ulong value, bool driver) =>
        driver
            ? value switch { 0 => "read", 1 => "write", 8 => "execute", _ => $"access {value}" }
            : (value & 0x8) != 0 ? "execute" : (value & 0x1) != 0 ? "write" : "read";

    /// <summary>DumpType of the kernel dump header.</summary>
    public static string? DumpTypeName(uint? dumpType) => dumpType switch
    {
        null => null,
        1 => "complete",
        2 => "kernel",
        4 => "small",
        5 => "complete (bitmap)",
        6 => "kernel (bitmap)",
        _ => $"type {dumpType}",
    };
}
