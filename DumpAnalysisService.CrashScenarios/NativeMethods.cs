using System.Runtime.InteropServices;

namespace DumpAnalysisService.CrashScenarios;

internal static unsafe class NativeMethods
{
    /// <summary>Application-defined exception code raised by the custom-exception scenario.</summary>
    public const uint CustomExceptionCode = 0xE0001234;

    /// <summary>Size of a CRITICAL_SECTION on x64.</summary>
    public const int CriticalSectionSize = 40;

    [DllImport("kernel32.dll", ExactSpelling = true)]
    public static extern void RaiseException(uint dwExceptionCode, uint dwExceptionFlags, uint nNumberOfArguments, nint lpArguments);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    public static extern void InitializeCriticalSection(void* lpCriticalSection);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    public static extern void EnterCriticalSection(void* lpCriticalSection);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    public static extern void DebugBreak();
}
