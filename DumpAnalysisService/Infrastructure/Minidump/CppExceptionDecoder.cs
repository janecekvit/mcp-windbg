namespace DumpAnalysisService.Infrastructure.Minidump;

/// <summary>
/// Recovers the thrown type (and std::exception::what()) of an MSVC C++ exception (0xE06D7363)
/// from captured memory, without symbols. The exception parameters are: magic number, thrown
/// object, ThrowInfo, and on 64-bit the image base that ThrowInfo's 32-bit RVAs are relative to.
/// ThrowInfo → CatchableTypeArray → CatchableType → TypeDescriptor, whose decorated name
/// (".?AVout_of_range@std@@") is undecorated here. Works when the module's data section and the
/// exception object are in the dump (full-memory dumps); otherwise returns null.
/// </summary>
public static class CppExceptionDecoder
{
    private const ulong MagicVc6 = 0x19930520;
    private const ulong MagicVc8 = 0x19930522;
    private const ulong MagicPure = 0x01994000;
    private const int MaxCatchableTypes = 32;

    internal static MinidumpCppException? Decode(MinidumpParser.MemoryMap memory, MinidumpException exception)
    {
        var p = exception.Parameters;
        if (p.Count < 3 || p[0] is not (>= MagicVc6 and <= MagicVc8 or MagicPure))
            return null;

        // Four parameters: 64-bit, ThrowInfo fields are RVAs from p[3]. Three: 32-bit absolute pointers.
        var is64 = p.Count >= 4;
        var imageBase = is64 ? p[3] : 0;
        var pointerSize = is64 ? 8 : 4;
        ulong Resolve(uint value) => is64 ? imageBase + value : value;

        // ThrowInfo: attributes, pmfnUnwind, pForwardCompat, pCatchableTypeArray (@12)
        if (memory.ReadUInt32(p[2] + 12) is not { } arrayRef || arrayRef == 0)
            return null;
        var array = Resolve(arrayRef);
        if (memory.ReadUInt32(array) is not { } count || count == 0)
            return null;

        var names = new List<string>();
        for (var i = 0u; i < Math.Min(count, MaxCatchableTypes); i++)
        {
            // CatchableType: properties, pType (@4), ...
            if (memory.ReadUInt32(array + 4 + i * 4) is not { } typeRef || typeRef == 0)
                continue;
            if (memory.ReadUInt32(Resolve(typeRef) + 4) is not { } descriptorRef || descriptorRef == 0)
                continue;
            // TypeDescriptor: pVFTable, spare, then the decorated name
            var decorated = memory.ReadAnsiString(Resolve(descriptorRef) + (ulong)(2 * pointerSize), 512);
            if (decorated is not null)
                names.Add(Undecorate(decorated));
        }

        if (names.Count == 0)
            return null;

        string? message = null;
        if (names.Contains("std::exception") && p[1] != 0
            && memory.ReadPointer(p[1] + (ulong)pointerSize, pointerSize) is { } what and not 0)
        {
            // std::exception: vfptr, then __std_exception_data { const char* _What; bool _DoFree; }
            message = memory.ReadAnsiString(what, 1024);
        }

        return new MinidumpCppException(names, message);
    }

    /// <summary>
    /// ".?AVout_of_range@std@@" → "std::out_of_range". Templates and other complex names
    /// (anything with '?' after the prefix) are returned decorated.
    /// </summary>
    public static string Undecorate(string decorated)
    {
        string[] prefixes = [".?AV", ".?AU", ".?AW4"];
        var prefix = prefixes.FirstOrDefault(decorated.StartsWith);
        if (prefix is null || !decorated.EndsWith("@@", StringComparison.Ordinal))
            return decorated;

        var body = decorated[prefix.Length..^2];
        if (body.Length == 0 || body.Contains('?'))
            return decorated;

        var parts = body.Split('@');
        Array.Reverse(parts);
        return string.Join("::", parts);
    }
}
