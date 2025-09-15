namespace BackgroundService.Models;

public record ExecuteCommandRequest(string SessionId, string Command);