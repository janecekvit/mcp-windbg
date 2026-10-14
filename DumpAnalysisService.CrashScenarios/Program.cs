using DumpAnalysisService.CrashScenarios;

if (args.Length != 1 || !Scenarios.All.TryGetValue(args[0], out var scenario))
{
    Console.Error.WriteLine($"Usage: CrashScenarios <scenario>  ({string.Join(", ", Scenarios.All.Keys)})");
    return 2;
}

// args.Length is 1 here, so this is 0 - but only at run time.
Scenarios.Initialize(args.Length - 1);
scenario();

// Every scenario faults or breaks into the debugger before it gets here.
Console.Error.WriteLine($"Scenario '{args[0]}' returned without faulting");
return 1;
