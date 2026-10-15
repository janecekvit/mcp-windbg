using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DumpAnalysisService.CrashScenarios;

/// <summary>
/// Crash scenarios. Every method on a fault path is NoInlining so that each one is a frame
/// of its own in the dump; the names are what the integration tests look for in the stack.
/// </summary>
internal static unsafe class Scenarios
{
    public static readonly IReadOnlyDictionary<string, Action> All = new Dictionary<string, Action>
    {
        ["access-violation"] = AccessViolationEntry,
        ["access-violation-alt-path"] = AlternatePathEntry,
        ["divide-by-zero"] = DivideByZeroEntry,
        ["stack-overflow"] = StackOverflowEntry,
        ["custom-exception"] = CustomExceptionEntry,
        ["fail-fast"] = FailFastEntry,
        ["managed-unhandled"] = ManagedUnhandledEntry,
        ["deadlock"] = DeadlockEntry,
        ["unloaded-module"] = UnloadedModuleEntry,
        ["use-after-free"] = UseAfterFreeEntry,
        ["cpp-exception"] = CppExceptionEntry,
        ["busy-loop"] = BusyLoopEntry,
        ["handle-leak"] = HandleLeakEntry,
    };

    // Set at run time from a value the compiler cannot know, so it cannot prove the pointer
    // is null or the divisor is zero and fold the faulting instruction away.
    private static int* s_nullPointer;
    private static int s_zero;
    private static volatile int s_sink;
    private static volatile bool s_keepSpinning = true;

    /// <param name="runtimeZero">Always 0 at run time (derived from the argument count).</param>
    public static void Initialize(int runtimeZero)
    {
        s_nullPointer = (int*)(nint)runtimeZero;
        s_zero = runtimeZero;
    }

    // ---- access-violation: AccessViolationEntry > PrepareRecord > ValidateRecord > ReadThroughPointer

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AccessViolationEntry() => PrepareRecord(s_nullPointer);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void PrepareRecord(int* record) => ValidateRecord(record);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ValidateRecord(int* record) => s_sink = ReadThroughPointer(record);

    /// <summary>The faulting function shared by both access-violation scenarios.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int ReadThroughPointer(int* value) => *value + 1;

    // ---- access-violation-alt-path: same faulting function, different callers

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AlternatePathEntry() => LoadFromCache(s_nullPointer);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void LoadFromCache(int* entry) => s_sink = ReadThroughPointer(entry);

    // ---- divide-by-zero: hardware #DE (integer division by a non-constant zero)

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DivideByZeroEntry() => s_sink = ComputeAverage(100, s_zero);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int ComputeAverage(int total, int count) => total / count;

    // ---- stack-overflow: unbounded recursion

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StackOverflowEntry() => s_sink = RecurseWithoutLimit(0);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RecurseWithoutLimit(int depth)
    {
        Span<byte> frame = stackalloc byte[256];
        frame[depth & 0xFF] = (byte)depth;
        // Using the result after the call prevents a tail call that would turn this into a loop.
        return RecurseWithoutLimit(depth + 1) + frame[0];
    }

    // ---- custom-exception: RaiseException with an application-defined code

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CustomExceptionEntry() => ReportInvalidState();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ReportInvalidState() =>
        NativeMethods.RaiseException(NativeMethods.CustomExceptionCode, 0, 0, 0);

    // ---- fail-fast: Environment.FailFast (fail-fast exception, bypasses all handlers)

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void FailFastEntry() => AbortOnCorruption();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AbortOnCorruption() =>
        Environment.FailFast("CrashScenarios: fail-fast scenario");

    // ---- managed-unhandled: unhandled .NET exception (NativeAOT raises it as SEH 0xE0434352)

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ManagedUnhandledEntry() => ParseConfiguration();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ParseConfiguration() =>
        throw new InvalidOperationException("CrashScenarios: unhandled managed exception");

    // ---- deadlock: two threads take two critical sections in opposite order

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DeadlockEntry()
    {
        var lockA = NativeMemory.AllocZeroed(NativeMethods.CriticalSectionSize);
        var lockB = NativeMemory.AllocZeroed(NativeMethods.CriticalSectionSize);
        NativeMethods.InitializeCriticalSection(lockA);
        NativeMethods.InitializeCriticalSection(lockB);

        using var bothHoldFirstLock = new CountdownEvent(2);
        StartLockThread("Deadlock-A", (nint)lockA, (nint)lockB, bothHoldFirstLock);
        StartLockThread("Deadlock-B", (nint)lockB, (nint)lockA, bothHoldFirstLock);

        bothHoldFirstLock.Wait();
        // Give both threads time to block on their second lock.
        Thread.Sleep(1000);

        // Hand control to the debugger, which writes the hang dump.
        NativeMethods.DebugBreak();
    }

    private static void StartLockThread(string name, nint first, nint second, CountdownEvent holdsFirst)
    {
        var thread = new Thread(() => AcquireInOrder((void*)first, (void*)second, holdsFirst))
        {
            IsBackground = true,
            Name = name,
        };
        thread.Start();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AcquireInOrder(void* first, void* second, CountdownEvent holdsFirst)
    {
        NativeMethods.EnterCriticalSection(first);
        holdsFirst.Signal();
        holdsFirst.Wait();
        NativeMethods.EnterCriticalSection(second);
    }

    // ---- unloaded-module: call into a DLL after FreeLibrary unmapped it

    /// <summary>System DLLs a bare NativeAOT process does not load on its own.</summary>
    private static readonly string[] s_unloadableDlls = ["dbghelp.dll", "version.dll", "winmm.dll", "wtsapi32.dll", "msimg32.dll"];

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void UnloadedModuleEntry() => CallIntoUnloadedPlugin();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CallIntoUnloadedPlugin()
    {
        foreach (var name in s_unloadableDlls)
        {
            if (NativeMethods.GetModuleHandleW(name) != 0)
                continue;
            var module = NativeMethods.LoadLibraryW(name);
            if (module == 0)
                continue;

            // A code address inside the image, kept after the image is gone (a dangling callback).
            var callback = (delegate* unmanaged<void>)(module + 0x1000);
            NativeMethods.FreeLibrary(module);
            if (NativeMethods.GetModuleHandleW(name) != 0)
                continue; // still referenced elsewhere, try the next one

            callback();
            return;
        }
        throw new InvalidOperationException("CrashScenarios: no DLL could be loaded and unloaded");
    }

    // ---- use-after-free: dereference a pointer read from a block carrying the debug CRT's freed fill (0xDD)

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void UseAfterFreeEntry() => s_sink = ReadReleasedRecord(ReleaseRecord());

    /// <summary>
    /// Fills the block the way the debug CRT's free() does. The block itself is kept allocated so the
    /// pattern is still there when it is read; the pointer field now holds 0xDDDDDDDDDDDDDDDD.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static nint* ReleaseRecord()
    {
        var record = (nint*)NativeMemory.Alloc(64);
        NativeMemory.Fill(record, 64, 0xDD);
        return record;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int ReadReleasedRecord(nint* record) => *(int*)record[0];

    // ---- cpp-exception: a real MSVC C++ throw (std::out_of_range) from msvcp140.dll

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CppExceptionEntry() => ThrowFromNativeLibrary();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowFromNativeLibrary()
    {
        fixed (byte* message = "CrashScenarios: out of range\0"u8)
            NativeMethods.ThrowOutOfRange(message);
    }

    // ---- busy-loop: one thread spins, then the debugger takes a hang dump

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void BusyLoopEntry()
    {
        var spinner = new Thread(SpinUntilStopped) { IsBackground = true, Name = "Spinner" };
        spinner.Start();
        Thread.Sleep(3000);
        NativeMethods.DebugBreak();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void SpinUntilStopped()
    {
        var iterations = 0;
        while (s_keepSpinning)
            iterations++;
        s_sink = iterations;
    }

    // ---- handle-leak: thousands of event handles that are never closed, then a hang dump

    public const int LeakedHandles = 12_000;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HandleLeakEntry()
    {
        LeakEventHandles();
        NativeMethods.DebugBreak();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void LeakEventHandles()
    {
        for (var i = 0; i < LeakedHandles; i++)
        {
            if (NativeMethods.CreateEventW(0, 1, 0, 0) == 0)
                throw new InvalidOperationException("CrashScenarios: CreateEvent failed");
        }
    }
}
