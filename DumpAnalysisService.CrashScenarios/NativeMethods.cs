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

    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    public static extern nint GetModuleHandleW(string lpModuleName);

    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    public static extern nint LoadLibraryW(string lpLibFileName);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    public static extern int FreeLibrary(nint hLibModule);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    public static extern nint CreateEventW(nint lpEventAttributes, int bManualReset, int bInitialState, nint lpName);

    /// <summary>std::_Xout_of_range(const char*): throws std::out_of_range (x64 decorated name).</summary>
    [DllImport("msvcp140.dll", EntryPoint = "?_Xout_of_range@std@@YAXPEBD@Z", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ThrowOutOfRange(byte* message);
}
