using Shared.Models;

namespace DumpAnalysisService.Services;

/// <summary>
/// First look at a dump straight from its minidump streams, without the debugger.
/// </summary>
public interface IDumpTriageService
{
    /// <exception cref="FileNotFoundException">The dump file does not exist.</exception>
    /// <exception cref="UnauthorizedAccessException">The dump is outside Security:AllowedDumpDirectories.</exception>
    /// <exception cref="InvalidDataException">The file is not a valid Windows dump.</exception>
    /// <param name="symbolCache">Symbol cache to check for the dump's PDBs; null uses the server default (as load_dump does).</param>
    Task<DumpTriageResult> TriageAsync(string dumpFilePath, string? symbolCache = null, CancellationToken cancellationToken = default);
}
