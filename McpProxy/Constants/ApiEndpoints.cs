namespace McpProxy.Constants;

public static class ApiEndpoints
{
    public const string Health = "/health";
    public const string LoadDump = "/api/load-dump";
    public const string ExecuteCommand = "/api/execute-command";
    public const string BasicAnalysis = "/api/basic-analysis";
    public const string PredefinedAnalysis = "/api/predefined-analysis";
    public const string Sessions = "/api/sessions";
    public const string DetectDebuggers = "/api/detect-debuggers";
    public const string Analyses = "/api/analyses";
    
    public static string SessionById(string sessionId) => $"/api/sessions/{sessionId}";
}

public static class ProgressValues
{
    public const double ValidationStart = 0.1;
    public const double ProcessingStart = 0.3;
    public const double ProcessingMiddle = 0.8;
    public const double ProcessingEnd = 0.9;
    public const double Completed = 1.0;
}