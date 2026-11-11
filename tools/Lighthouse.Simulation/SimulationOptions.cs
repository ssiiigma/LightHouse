using System.Globalization;
using Lighthouse.Core.AI;

namespace Lighthouse.Simulation;

/// <summary>Параметри експерименту; значення за замовчуванням відтворювані (фіксований seed).</summary>
public sealed record SimulationOptions(int Users, int Rounds, int TasksPerRound, int Seed, string OutputDir)
{
    public static SimulationOptions Default { get; } = new(200, 300, 15, 42, "simulation-results");

    public static string Usage =>
        "Параметри: --users N (200) --rounds N (300) --tasks N (15) --seed N (42) --out DIR (simulation-results)";

    public static SimulationOptions Parse(string[] args)
    {
        var options = Default;

        for (var i = 0; i < args.Length; i += 2)
        {
            var key = args[i];
            var value = i + 1 < args.Length
                ? args[i + 1]
                : throw new ArgumentException($"Не вказано значення для {key}.");

            options = key switch
            {
                "--users" => options with { Users = ParseInt(key, value) },
                "--rounds" => options with { Rounds = ParseInt(key, value) },
                "--tasks" => options with { TasksPerRound = ParseInt(key, value) },
                "--seed" => options with { Seed = ParseInt(key, value) },
                "--out" => options with { OutputDir = value },
                _ => throw new ArgumentException($"Невідомий параметр {key}.")
            };
        }

        if (options.Users < 1 || options.Rounds < 1)
        {
            throw new ArgumentException("--users і --rounds мають бути не меншими за 1.");
        }

        if (options.TasksPerRound < LinUcbDefaults.TopK)
        {
            throw new ArgumentException($"--tasks має бути не меншим за {LinUcbDefaults.TopK}.");
        }

        return options;
    }

    private static int ParseInt(string key, string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : throw new ArgumentException($"Некоректне значення «{value}» для {key}.");
}