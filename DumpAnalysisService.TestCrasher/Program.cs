using DumpAnalysisService.TestCrasher.CrashScenarios;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: TestCrasher <output-dump-path> [scenario]");
    return 2;
}

var dumpPath = args[0];
var scenario = args.Length > 1 ? args[1] : "default";

try
{
    switch (scenario)
    {
        case "default":
            DefaultScenario.Run(dumpPath);
            break;
        default:
            Console.Error.WriteLine($"Unknown scenario: {scenario}");
            return 3;
    }
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Crasher failed: {ex}");
    return 1;
}
