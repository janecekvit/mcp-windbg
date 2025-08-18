namespace CdbBackgroundService;

public static class PredefinedAnalyses
{
    public static readonly Dictionary<string, string[]> Analyses = new()
    {
        ["basic"] = new[]
        {
            ".echo ---------- BASIC INFO ----------",
            "version",
            "!peb",
            "~",
            ".echo ---------- EXCEPTION CONTEXT ----------", 
            ".ecxr",
            ".echo ---------- ANALYZE ----------",
            "!analyze -v",
            ".echo ---------- ALL THREAD STACKS ----------",
            "~* kb"
        },

        ["exception"] = new[]
        {
            ".echo ---------- EXCEPTION ANALYSIS ----------",
            ".ecxr",
            "!analyze -v",
            ".echo ---------- EXCEPTION RECORD ----------",
            "!exr -1",
            ".echo ---------- CONTEXT RECORD ----------", 
            "!cxr -1",
            ".echo ---------- CURRENT THREAD STACK ----------",
            "kb"
        },

        ["threads"] = new[]
        {
            ".echo ---------- ALL THREADS ----------",
            "~",
            ".echo ---------- ALL THREAD STACKS ----------",
            "~* kb",
            ".echo ---------- THREAD DETAILS ----------",
            "~* !teb"
        },

        ["heap"] = new[]
        {
            ".echo ---------- HEAP INFORMATION ----------",
            "!heap -s",
            ".echo ---------- HEAP SUMMARY ----------",
            "!heap -stat -h 0",
            ".echo ---------- HEAP VALIDATION ----------",
            "!heap -x"
        },

        ["modules"] = new[]
        {
            ".echo ---------- LOADED MODULES ----------",
            "lm",
            ".echo ---------- MODULE DETAILS ----------",
            "lmv",
            ".echo ---------- UNLOADED MODULES ----------",
            "lmu"
        },

        ["handles"] = new[]
        {
            ".echo ---------- HANDLE INFORMATION ----------",
            "!handle",
            ".echo ---------- HANDLE SUMMARY ----------",
            "!handle 0 0",
            ".echo ---------- PROCESS HANDLES ----------",
            "!handle -p"
        },

        ["locks"] = new[]
        {
            ".echo ---------- CRITICAL SECTIONS ----------",
            "!locks",
            ".echo ---------- DEADLOCK DETECTION ----------",
            "!deadlock",
            ".echo ---------- THREAD BLOCKING ----------",
            "!cs -l"
        },

        ["memory"] = new[]
        {
            ".echo ---------- VIRTUAL MEMORY ----------",
            "!vm",
            ".echo ---------- ADDRESS SPACE ----------",
            "!address",
            ".echo ---------- MEMORY USAGE ----------",
            "!address -summary"
        },

        ["drivers"] = new[]
        {
            ".echo ---------- LOADED DRIVERS ----------",
            "!drvobj",
            ".echo ---------- DRIVER INFORMATION ----------",
            "!drivers",
            ".echo ---------- DEVICE OBJECTS ----------",
            "!devobj"
        },

        ["processes"] = new[]
        {
            ".echo ---------- PROCESS INFORMATION ----------",
            "!process 0 0",
            ".echo ---------- CURRENT PROCESS ----------",
            "!process -1 7",
            ".echo ---------- PROCESS TREE ----------",
            "!peb"
        }
    };

    public static string[] GetAnalysisCommands(string analysisName)
    {
        return Analyses.TryGetValue(analysisName.ToLowerInvariant(), out var commands) 
            ? commands 
            : Array.Empty<string>();
    }

    public static IEnumerable<string> GetAvailableAnalyses()
    {
        return Analyses.Keys;
    }

    public static string GetAnalysisDescription(string analysisName)
    {
        return analysisName.ToLowerInvariant() switch
        {
            "basic" => "Comprehensive basic analysis including exception context, analyze -v, and thread stacks",
            "exception" => "Detailed exception analysis with exception and context records",
            "threads" => "Complete thread analysis including all thread information and stacks",
            "heap" => "Heap analysis including statistics, summary, and validation",
            "modules" => "Module analysis including loaded, detailed, and unloaded modules",
            "handles" => "Handle analysis including handle information and process handles",
            "locks" => "Lock analysis including critical sections and deadlock detection",
            "memory" => "Memory analysis including virtual memory and address space",
            "drivers" => "Driver analysis including loaded drivers and device objects",
            "processes" => "Process analysis including process information and tree",
            _ => "Unknown analysis type"
        };
    }
}