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
    };

    // Set at run time from a value the compiler cannot know, so it cannot prove the pointer
    // is null or the divisor is zero and fold the faulting instruction away.
    private static int* s_nullPointer;
    private static int s_zero;
    private static volatile int s_sink;

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
}
