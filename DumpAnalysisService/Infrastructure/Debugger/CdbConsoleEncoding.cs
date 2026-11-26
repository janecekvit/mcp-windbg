using System.Runtime.InteropServices;
using System.Text;

namespace DumpAnalysisService.Infrastructure.Debugger;

/// <summary>
/// The encoding cdb.exe uses on redirected stdin/stdout/stderr: the system ANSI code page
/// (e.g. 1250 or 1252), not UTF-8. Decoding its output as UTF-8 turns every non-ASCII
/// character, such as one in a dump path, into U+FFFD. When the ANSI code page is 65001 this is
/// UTF-8; on other systems (the fake debugger in unit tests) it is UTF-8 too.
/// </summary>
public static class CdbConsoleEncoding
{
    private const int Utf8CodePage = 65001;

    private static readonly Lazy<Encoding> _current = new(() =>
        OperatingSystem.IsWindows() ? ForCodePage(NativeMethods.GetACP()) : ForCodePage(Utf8CodePage));

    /// <summary>The encoding for this system's ANSI code page.</summary>
    public static Encoding Current => _current.Value;

    /// <summary>The encoding for a Windows code page, without a byte order mark.</summary>
    public static Encoding ForCodePage(int codePage)
    {
        if (codePage == Utf8CodePage)
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        // Legacy code pages (1250, 1252, 932, ...) come from the code pages provider.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(codePage);
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", ExactSpelling = true)]
        internal static extern int GetACP();
    }
}
