using Shared;

namespace BackgroundService.Services;

public class AnalysisService : IAnalysisService
{
    private static readonly Dictionary<AnalysisType, IReadOnlyList<string>> Analyses = new()
    {
        [AnalysisType.Basic] = new[]
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

        [AnalysisType.Exception] = new[]
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

        [AnalysisType.Threads] = new[]
        {
            ".echo ---------- ALL THREADS ----------",
            "~",
            ".echo ---------- ALL THREAD STACKS ----------",
            "~* kb",
            ".echo ---------- THREAD DETAILS ----------",
            "~* !teb"
        },

        [AnalysisType.Heap] = new[]
        {
            ".echo ---------- HEAP INFORMATION ----------",
            "!heap -s",
            ".echo ---------- HEAP SUMMARY ----------",
            "!heap -stat -h 0",
            ".echo ---------- HEAP VALIDATION ----------",
            "!heap -x"
        },

        [AnalysisType.Modules] = new[]
        {
            ".echo ---------- LOADED MODULES ----------",
            "lm",
            ".echo ---------- MODULE DETAILS ----------",
            "lmv",
            ".echo ---------- UNLOADED MODULES ----------",
            "lmu"
        },

        [AnalysisType.Handles] = new[]
        {
            ".echo ---------- HANDLE INFORMATION ----------",
            "!handle",
            ".echo ---------- HANDLE SUMMARY ----------",
            "!handle 0 0",
            ".echo ---------- PROCESS HANDLES ----------",
            "!handle -p"
        },

        [AnalysisType.Locks] = new[]
        {
            ".echo ---------- CRITICAL SECTIONS ----------",
            "!locks",
            ".echo ---------- DEADLOCK DETECTION ----------",
            "!deadlock",
            ".echo ---------- THREAD BLOCKING ----------",
            "!cs -l"
        },

        [AnalysisType.Memory] = new[]
        {
            ".echo ---------- VIRTUAL MEMORY ----------",
            "!vm",
            ".echo ---------- ADDRESS SPACE ----------",
            "!address",
            ".echo ---------- MEMORY USAGE ----------",
            "!address -summary"
        },

        [AnalysisType.Drivers] = new[]
        {
            ".echo ---------- LOADED DRIVERS ----------",
            "!drvobj",
            ".echo ---------- DRIVER INFORMATION ----------",
            "!drivers",
            ".echo ---------- DEVICE OBJECTS ----------",
            "!devobj"
        },

        [AnalysisType.Processes] = new[]
        {
            ".echo ---------- PROCESS INFORMATION ----------",
            "!process 0 0",
            ".echo ---------- CURRENT PROCESS ----------",
            "!process -1 7",
            ".echo ---------- PROCESS TREE ----------",
            "!peb"
        }
    };

    public IReadOnlyList<string> GetAnalysisCommands(string analysisName)
    {
        try
        {
            var analysisType = AnalysisTypeExtensions.ToAnalysisType(analysisName);
            return Analyses.TryGetValue(analysisType, out var commands) ? commands : Array.Empty<string>();
        }
        catch (ArgumentException)
        {
            return Array.Empty<string>();
        }
    }

    public IEnumerable<string> GetAvailableAnalyses()
    {
        return Analyses.Keys.Select(AnalysisTypeExtensions.ToString);
    }

    public string GetAnalysisDescription(string analysisName)
    {
        try
        {
            var analysisType = AnalysisTypeExtensions.ToAnalysisType(analysisName);
            return analysisType.GetDescription();
        }
        catch (ArgumentException)
        {
            return "Unknown analysis type";
        }
    }
}