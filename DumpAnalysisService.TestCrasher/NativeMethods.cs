using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DumpAnalysisService.TestCrasher;

[Flags]
internal enum MiniDumpType : uint
{
    Normal = 0x00000000,
    WithDataSegs = 0x00000001,
    WithFullMemory = 0x00000002,
    WithHandleData = 0x00000004,
    WithUnloadedModules = 0x00000020,
    WithFullMemoryInfo = 0x00000800,
    WithThreadInfo = 0x00001000,
    WithTokenInformation = 0x00040000,
}

internal static class NativeMethods
{
    [DllImport("dbghelp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MiniDumpWriteDump(
        IntPtr hProcess,
        uint processId,
        SafeFileHandle hFile,
        MiniDumpType dumpType,
        IntPtr expParam,
        IntPtr userStreamParam,
        IntPtr callbackParam);

    public static void WriteDump(string outputPath, MiniDumpType type)
    {
        using var stream = File.Create(outputPath);
        var process = System.Diagnostics.Process.GetCurrentProcess();
        var ok = MiniDumpWriteDump(
            process.Handle,
            (uint)process.Id,
            stream.SafeFileHandle!,
            type,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);
        if (!ok)
        {
            var err = Marshal.GetLastPInvokeError();
            throw new InvalidOperationException(
                $"MiniDumpWriteDump failed with error 0x{err:X8}");
        }
    }
}
