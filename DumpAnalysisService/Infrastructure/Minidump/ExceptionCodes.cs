namespace DumpAnalysisService.Infrastructure.Minidump;

/// <summary>
/// Windows exception codes that commonly end a process, with their symbolic names.
/// </summary>
public static class ExceptionCodes
{
    public const uint AccessViolation = 0xC0000005;
    public const uint InPageError = 0xC0000006;
    public const uint StackOverflow = 0xC00000FD;
    public const uint IntegerDivideByZero = 0xC0000094;
    public const uint FailFast = 0xC0000409;
    public const uint FailFastException = 0xC0000602;
    public const uint HeapCorruption = 0xC0000374;
    public const uint Breakpoint = 0x80000003;
    public const uint ClrException = 0xE0434352;
    public const uint CppException = 0xE06D7363;

    private static readonly Dictionary<uint, string> _names = new()
    {
        [AccessViolation] = "EXCEPTION_ACCESS_VIOLATION",
        [InPageError] = "EXCEPTION_IN_PAGE_ERROR",
        [0xC0000008] = "EXCEPTION_INVALID_HANDLE",
        [0xC0000017] = "STATUS_NO_MEMORY",
        [0xC000001D] = "EXCEPTION_ILLEGAL_INSTRUCTION",
        [0xC0000025] = "EXCEPTION_NONCONTINUABLE_EXCEPTION",
        [0xC000008C] = "EXCEPTION_ARRAY_BOUNDS_EXCEEDED",
        [0xC000008E] = "EXCEPTION_FLT_DIVIDE_BY_ZERO",
        [IntegerDivideByZero] = "EXCEPTION_INT_DIVIDE_BY_ZERO",
        [0xC0000095] = "EXCEPTION_INT_OVERFLOW",
        [0xC0000096] = "EXCEPTION_PRIV_INSTRUCTION",
        [StackOverflow] = "EXCEPTION_STACK_OVERFLOW",
        [0xC0000135] = "STATUS_DLL_NOT_FOUND",
        [0xC0000142] = "STATUS_DLL_INIT_FAILED",
        [HeapCorruption] = "STATUS_HEAP_CORRUPTION",
        [FailFast] = "STATUS_STACK_BUFFER_OVERRUN (fail-fast)",
        [0xC0000420] = "STATUS_ASSERTION_FAILURE",
        [FailFastException] = "STATUS_FAIL_FAST_EXCEPTION",
        [0x80000002] = "EXCEPTION_DATATYPE_MISALIGNMENT",
        [Breakpoint] = "EXCEPTION_BREAKPOINT",
        [0x80000004] = "EXCEPTION_SINGLE_STEP",
        [ClrException] = "CLR_EXCEPTION (.NET)",
        [CppException] = "CPP_EH_EXCEPTION (C++ throw)",
    };

    public static string NameOf(uint code) => _names.TryGetValue(code, out var name) ? name : "unknown";
}

/// <summary>
/// Common kernel bug check codes.
/// </summary>
public static class BugCheckCodes
{
    private static readonly Dictionary<uint, string> _names = new()
    {
        [0x0A] = "IRQL_NOT_LESS_OR_EQUAL",
        [0x1E] = "KMODE_EXCEPTION_NOT_HANDLED",
        [0x3B] = "SYSTEM_SERVICE_EXCEPTION",
        [0x50] = "PAGE_FAULT_IN_NONPAGED_AREA",
        [0x7E] = "SYSTEM_THREAD_EXCEPTION_NOT_HANDLED",
        [0x7F] = "UNEXPECTED_KERNEL_MODE_TRAP",
        [0x9F] = "DRIVER_POWER_STATE_FAILURE",
        [0xC2] = "BAD_POOL_CALLER",
        [0xD1] = "DRIVER_IRQL_NOT_LESS_OR_EQUAL",
        [0xEF] = "CRITICAL_PROCESS_DIED",
        [0x133] = "DPC_WATCHDOG_VIOLATION",
        [0x139] = "KERNEL_SECURITY_CHECK_FAILURE",
        [0x1A] = "MEMORY_MANAGEMENT",
        [0x19] = "BAD_POOL_HEADER",
        [0xE2] = "MANUALLY_INITIATED_CRASH",
    };

    public static string? NameOf(uint code) => _names.GetValueOrDefault(code);
}
