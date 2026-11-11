using System.Globalization;
using System.Text;

namespace Lighthouse.Simulation;

/// <summary>Запис результатів: rounds.csv (динаміка), users.csv (по користувачах), summary.csv та summary.md.</summary>
public static class Report
{
    private static readonly string[] DisplayNames =
        ["Випадковий порядок", "Евристика", "LinUCB", "Оракул (верхня межа)"];

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private sealed record UserMetrics(double Reward, double Acceptance, double LateReward);

    public static string Write(SimulationOptions options, IReadOnlyList<UserRun> runs)
    {
        Directory.CreateDirectory(options.OutputDir);

        var policies = Enum.GetValues<Policy>();
        var lateStart = options.Rounds * 3 / 4; // останні 25 % раундів — після навчання моделі

        var metrics = policies.ToDictionary(
            p => p,
            p => runs.Select(run => new UserMetrics(
                run.Reward[(int)p].Average(),
                run.Accepted[(int)p].Average(),
                run.Reward[(int)p].Skip(lateStart).Average())).ToArray());

        WriteRounds(options, runs, policies);
        WriteUsers(options, metrics, policies);

        var oracleMean = metrics[Policy.Oracle].Average(m => m.Reward);

        var csv = new StringBuilder(
            "policy,mean_reward,mean_reward_ci95,acceptance_rate,acceptance_ci95,late_mean_reward,late_ci95,share_of_oracle\n");
        var md = new StringBuilder();
        md.AppendLine($"Параметри: користувачів {options.Users}, раундів {options.Rounds}, " +
                      $"завдань у раунді {options.TasksPerRound}, seed {options.Seed}, показується топ-3.");
        md.AppendLine();
        md.AppendLine("| Стратегія | Середня винагорода | Частка прийнятих у топ-3 | Винагорода в останніх 25 % раундів | % від еталона |");
        md.AppendLine("|---|---|---|---|---|");

        foreach (var policy in policies)
        {
            var items = metrics[policy];
            var reward = Stats(items.Select(m => m.Reward));
            var acceptance = Stats(items.Select(m => m.Acceptance));
            var late = Stats(items.Select(m => m.LateReward));
            var share = reward.Mean / oracleMean * 100.0;

            md.AppendLine($"| {DisplayNames[(int)policy]} | {Format(reward)} | {Format(acceptance)} | {Format(late)} | {share.ToString("F1", Inv)} % |");
            csv.AppendLine(string.Join(',',
                policy,
                reward.Mean.ToString("F6", Inv), reward.Ci.ToString("F6", Inv),
                acceptance.Mean.ToString("F6", Inv), acceptance.Ci.ToString("F6", Inv),
                late.Mean.ToString("F6", Inv), late.Ci.ToString("F6", Inv),
                share.ToString("F2", Inv)));
        }

        md.AppendLine();
        md.AppendLine("Значення — середнє по користувачах ± півширина 95 % довірчого інтервалу.");
        md.AppendLine();
        md.AppendLine("Парні різниці (за користувачами):");
        md.AppendLine();
        md.AppendLine(Pair("LinUCB − евристика", metrics[Policy.LinUcb], metrics[Policy.Heuristic]));
        md.AppendLine(Pair("LinUCB − випадковий порядок", metrics[Policy.LinUcb], metrics[Policy.Random]));
        md.AppendLine(Pair("Евристика − випадковий порядок", metrics[Policy.Heuristic], metrics[Policy.Random]));

        File.WriteAllText(Path.Combine(options.OutputDir, "summary.csv"), csv.ToString());
        File.WriteAllText(Path.Combine(options.OutputDir, "summary.md"), md.ToString());
        return md.ToString();
    }

    private static string Pair(string label, UserMetrics[] first, UserMetrics[] second)
    {
        var overall = Stats(first.Zip(second, (a, b) => a.Reward - b.Reward));
        var late = Stats(first.Zip(second, (a, b) => a.LateReward - b.LateReward));
        return $"- {label}: {Format(overall)} (весь експеримент), {Format(late)} (останні 25 % раундів)";
    }

    private static void WriteRounds(SimulationOptions options, IReadOnlyList<UserRun> runs, Policy[] policies)
    {
        var sb = new StringBuilder("round,policy,mean_reward,acceptance_rate\n");
        for (var round = 0; round < options.Rounds; round++)
        {
            foreach (var policy in policies)
            {
                var index = (int)policy;
                sb.Append((round + 1).ToString(Inv)).Append(',')
                    .Append(policy).Append(',')
                    .Append(runs.Average(r => r.Reward[index][round]).ToString("F6", Inv)).Append(',')
                    .Append(runs.Average(r => r.Accepted[index][round]).ToString("F6", Inv)).Append('\n');
            }
        }

        File.WriteAllText(Path.Combine(options.OutputDir, "rounds.csv"), sb.ToString());
    }

    private static void WriteUsers(
        SimulationOptions options, Dictionary<Policy, UserMetrics[]> metrics, Policy[] policies)
    {
        var sb = new StringBuilder("user,policy,mean_reward,acceptance_rate,late_mean_reward\n");
        for (var user = 0; user < options.Users; user++)
        {
            foreach (var policy in policies)
            {
                var m = metrics[policy][user];
                sb.Append((user + 1).ToString(Inv)).Append(',')
                    .Append(policy).Append(',')
                    .Append(m.Reward.ToString("F6", Inv)).Append(',')
                    .Append(m.Acceptance.ToString("F6", Inv)).Append(',')
                    .Append(m.LateReward.ToString("F6", Inv)).Append('\n');
            }
        }

        File.WriteAllText(Path.Combine(options.OutputDir, "users.csv"), sb.ToString());
    }

    private static (double Mean, double Ci) Stats(IEnumerable<double> values)
    {
        var array = values.ToArray();
        var mean = array.Average();
        if (array.Length < 2)
        {
            return (mean, 0.0);
        }

        var variance = array.Sum(v => (v - mean) * (v - mean)) / (array.Length - 1);
        return (mean, 1.96 * Math.Sqrt(variance / array.Length));
    }

    private static string Format((double Mean, double Ci) stats) =>
        $"{stats.Mean.ToString("F3", Inv)} ± {stats.Ci.ToString("F3", Inv)}";
}