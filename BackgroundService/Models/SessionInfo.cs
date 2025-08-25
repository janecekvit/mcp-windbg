namespace CdbBackgroundService.Models;

public class SessionInfo
{
    public required string SessionId { get; init; }
    public required string DumpFile { get; init; }
    public required bool IsActive { get; init; }
}