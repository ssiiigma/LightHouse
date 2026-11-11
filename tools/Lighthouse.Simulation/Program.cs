using Lighthouse.Simulation;

SimulationOptions options;
try
{
    options = SimulationOptions.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine(SimulationOptions.Usage);
    return 2;
}

Console.WriteLine(
    $"Імітація: {options.Users} користувачів × {options.Rounds} раундів, " +
    $"{options.TasksPerRound} завдань у раунді, seed {options.Seed}...");

var started = DateTime.UtcNow;
var runs = Simulator.Run(options);
var summary = Report.Write(options, runs);

Console.WriteLine();
Console.WriteLine(summary);
Console.WriteLine($"Файли: {Path.GetFullPath(options.OutputDir)} (rounds.csv, users.csv, summary.csv, summary.md)");
Console.WriteLine($"Час виконання: {(DateTime.UtcNow - started).TotalSeconds:F1} с");
return 0;