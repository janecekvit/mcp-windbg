using DumpAnalysisService.Infrastructure.Detection;
using DumpAnalysisService.Infrastructure.IO;
using Microsoft.Extensions.Logging.Abstractions;

namespace DumpAnalysisService.IntegrationTests.Fixtures;

internal static class CdbAvailability
{
    private static readonly Lazy<bool> _isAvailable = new(Probe);

    public static bool IsAvailable => _isAvailable.Value;

    public const string SkipReason =
        "CDB (cdb.exe) not installed. Install Windows Debugging Tools to run this test.";

    private static bool Probe()
    {
        try
        {
            var expansion = new PathExpansionService(
                NullLogger<PathExpansionService>.Instance);
            var detection = new PathDetectionService(
                NullLogger<PathDetectionService>.Instance,
                expansion);
            var paths = detection.DetectDebuggerPaths();
            return paths.Count > 0;
        }
        catch
        {
            return false;
        }
    }
}
