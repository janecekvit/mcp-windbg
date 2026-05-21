namespace DumpAnalysisService.TestCrasher.CrashScenarios;

internal static class DefaultScenario
{
    public static void Run(string dumpPath)
    {
        // Allocate ~10 MB across SOH and LOH so heap analysis has data.
        var sohBuffers = new List<byte[]>();
        for (var i = 0; i < 1000; i++)
            sohBuffers.Add(new byte[1024]);          // ~1 MB total SOH
        var lohBuffer = new byte[9 * 1024 * 1024];   // ~9 MB LOH
        lohBuffer[0] = 1; // prevent dead-store elimination

        // Spawn 10 parked threads so thread analysis has data.
        using var ready = new CountdownEvent(10);
        using var hold = new ManualResetEventSlim(false);
        var threads = new List<Thread>();
        for (var i = 0; i < 10; i++)
        {
            var t = new Thread(() =>
            {
                ready.Signal();
                hold.Wait();
            }) { IsBackground = true, Name = $"Crasher-Worker-{i}" };
            threads.Add(t);
            t.Start();
        }
        ready.Wait();

        // Build a stack >= 5 frames deep before dumping. The buffers are
        // passed down so they stay referenced (and live in the dump) right
        // up to the MiniDumpWriteDump call.
        BuildStackAndDump(dumpPath, depth: 5, sohBuffers, lohBuffer);

        // Release the parked threads so the process can exit cleanly.
        hold.Set();
        foreach (var t in threads) t.Join();
    }

    private static void BuildStackAndDump(
        string dumpPath, int depth, List<byte[]> heapRefsInScope, byte[] lohRefInScope)
    {
        if (depth > 0)
        {
            BuildStackAndDump(dumpPath, depth - 1, heapRefsInScope, lohRefInScope);
            return;
        }
        NativeMethods.WriteDump(dumpPath, MiniDumpType.WithFullMemory
                                          | MiniDumpType.WithHandleData
                                          | MiniDumpType.WithThreadInfo
                                          | MiniDumpType.WithFullMemoryInfo);
    }
}
