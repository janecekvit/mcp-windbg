namespace CdbBackgroundService.Models;

public record ExecuteCommandRequest(string SessionId, string Command);