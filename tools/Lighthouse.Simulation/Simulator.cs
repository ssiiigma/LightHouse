using Lighthouse.Core.AI;
using Lighthouse.Core.Entities;
using Lighthouse.Shared.Enums;

namespace Lighthouse.Simulation;

public enum Policy
{
    Random = 0,
    Heuristic = 1,
    LinUcb = 2,

    /// <summary>Еталон: топ-3 за істинною ймовірністю прийняття (верхня межа, не конкурент).</summary>
    Oracle = 3
}

/// <summary>Результати одного користувача: [стратегія][раунд].</summary>
public sealed class UserRun(int policies, int rounds)
{
    /// <summary>Середня істинна ймовірність прийняття показаних завдань.</summary>
    public double[][] Reward { get; } = NewMatrix(policies, rounds);

    /// <summary>Реалізована частка прийнятих серед показаних.</summary>
    public double[][] Accepted { get; } = NewMatrix(policies, rounds);

    private static double[][] NewMatrix(int rows, int columns) =>
        Enumerable.Range(0, rows).Select(_ => new double[columns]).ToArray();
}

/// <summary>
/// Імітація користувачів із прихованим вектором уподобань. Усі стратегії бачать ті самі завдання й ті самі
/// випадкові «кидки» прийняття (common random numbers), тому різниця між ними не залежить від везіння.
/// </summary>
public static class Simulator
{
    private const int Categories = 5;
    private const int Top = LinUcbDefaults.TopK;
    private const double PreferenceSpread = 1.2;

    /// <summary>Середні уподобань у популяції: слабкий зсув до пріоритету й терміновості (x1…x4), решта — 0.</summary>
    private static readonly double[] PopulationMean = [-0.5, 1.0, 1.0, 0.5, 0, 0, 0, 0, 0, 0];

    // Понеділок, 5 січня 2026.
    private static readonly DateTime Start = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

    public static int PolicyCount { get; } = Enum.GetValues<Policy>().Length;

    public static IReadOnlyList<UserRun> Run(SimulationOptions options)
    {
        var runs = new UserRun[options.Users];
        Parallel.For(0, options.Users, user => runs[user] = RunUser(user, options));
        return runs;
    }

    private static UserRun RunUser(int userIndex, SimulationOptions options)
    {
        var rng = new Random(unchecked(options.Seed * 7919 + userIndex * 104729 + 17));
        var run = new UserRun(PolicyCount, options.Rounds);
        var zone = TimeZoneInfo.Utc;

        var weights = new double[LinUcbDefaults.Dimension];
        for (var j = 0; j < weights.Length; j++)
        {
            weights[j] = PopulationMean[j] + PreferenceSpread * Gaussian(rng);
        }

        // Статична історія користувача (її знає система): частки виконаних за інтервалами доби, днями й категоріями.
        var categoryIds = Enumerable.Range(0, Categories).Select(_ => NextGuid(rng)).ToArray();
        var hourBuckets = Enumerable.Range(0, UserHistory.HourBuckets).Select(_ => RandomCount(rng)).ToList();
        var weekDays = Enumerable.Range(0, UserHistory.WeekDays).Select(_ => RandomCount(rng)).ToList();
        var categoryCounts = new Dictionary<Guid, OutcomeCount>();
        foreach (var id in categoryIds)
        {
            categoryCounts[id] = RandomCount(rng);
        }

        var prioritizer = new Prioritizer();
        var model = new LinUcbModel(LinUcbDefaults.Dimension, LinUcbDefaults.Alpha);
        var feedbackCount = 0;

        for (var round = 0; round < options.Rounds; round++)
        {
            var now = Start.AddDays(round).AddHours(rng.Next(24));

            var tasks = new List<TaskItem>(options.TasksPerRound);
            var postpones = new Dictionary<Guid, int>();
            for (var i = 0; i < options.TasksPerRound; i++)
            {
                var task = new TaskItem
                {
                    Id = NextGuid(rng),
                    Priority = (TaskPriority)rng.Next(4),
                    DeadlineUtc = rng.NextDouble() < 0.4 ? null : now.AddHours(rng.NextDouble() * 264 - 24),
                    EstimatedMinutes = rng.NextDouble() < 0.5 ? null : 15 + rng.Next(226),
                    ReminderAtUtc = rng.NextDouble() < 0.3 ? now.AddHours(1) : null,
                    CategoryId = rng.NextDouble() < 0.8 ? categoryIds[rng.Next(Categories)] : null
                };
                tasks.Add(task);

                if (rng.NextDouble() < 0.25)
                {
                    postpones[task.Id] = rng.Next(1, 6);
                }
            }

            var history = new UserHistory
            {
                ByHourBucket = hourBuckets,
                ByWeekDay = weekDays,
                ByCategory = categoryCounts,
                PostponeCounts = postpones
            };

            var contexts = tasks.Select(t => TaskFeatureExtractor.Extract(t, history, now, zone)).ToArray();
            var probability = contexts.Select(x => Sigmoid(Dot(weights, x))).ToArray();
            var accepted = new bool[tasks.Count];
            for (var i = 0; i < accepted.Length; i++)
            {
                accepted[i] = rng.NextDouble() < probability[i]; // один «кидок» на завдання для всіх стратегій
            }

            var indexById = new Dictionary<Guid, int>();
            for (var i = 0; i < tasks.Count; i++)
            {
                indexById[tasks[i].Id] = i;
            }

            // 1) Випадковий порядок.
            var random = Enumerable.Range(0, tasks.Count).OrderBy(_ => rng.Next()).Take(Top).ToArray();
            Record(run, Policy.Random, round, random, probability, accepted);

            // 2) Евристика h = 0.45·x2 + 0.45·x3 + 0.10·x4 (двошарове ранжування Prioritizer без моделі).
            var heuristic = prioritizer.Rank(new PrioritizationRequest(tasks, history, null, 0, now, zone));
            Record(run, Policy.Heuristic, round, TopIndexes(heuristic, indexById), probability, accepted);

            // 3) LinUCB: холодний старт на евристиці до N_min відгуків, далі модель; навчання на топ-3.
            var linUcb = prioritizer.Rank(new PrioritizationRequest(tasks, history, model, feedbackCount, now, zone));
            var shown = linUcb.Ranked.Take(Top).ToList();
            Record(run, Policy.LinUcb, round, shown.Select(r => indexById[r.TaskId]).ToArray(), probability, accepted);
            foreach (var ranked in shown)
            {
                model.Update(ranked.Context, accepted[indexById[ranked.TaskId]] ? 1.0 : 0.0);
                feedbackCount++;
            }

            // 4) Оракул.
            var oracle = Enumerable.Range(0, tasks.Count).OrderByDescending(i => probability[i]).Take(Top).ToArray();
            Record(run, Policy.Oracle, round, oracle, probability, accepted);
        }

        return run;
    }

    private static int[] TopIndexes(PrioritizationResult result, Dictionary<Guid, int> indexById) =>
        result.Ranked.Take(Top).Select(r => indexById[r.TaskId]).ToArray();

    private static void Record(
        UserRun run, Policy policy, int round, int[] shown, double[] probability, bool[] accepted)
    {
        run.Reward[(int)policy][round] = shown.Average(i => probability[i]);
        run.Accepted[(int)policy][round] = shown.Count(i => accepted[i]) / (double)shown.Length;
    }

    private static OutcomeCount RandomCount(Random rng)
    {
        var total = rng.Next(5, 31);
        var completed = (int)Math.Round(total * rng.NextDouble());
        return new OutcomeCount(completed, total);
    }

    private static Guid NextGuid(Random rng)
    {
        var bytes = new byte[16];
        rng.NextBytes(bytes);
        return new Guid(bytes);
    }

    private static double Gaussian(Random rng)
    {
        var u1 = 1.0 - rng.NextDouble();
        var u2 = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    private static double Sigmoid(double value) => 1.0 / (1.0 + Math.Exp(-value));

    private static double Dot(double[] left, double[] right)
    {
        var sum = 0.0;
        for (var i = 0; i < left.Length; i++)
        {
            sum += left[i] * right[i];
        }

        return sum;
    }
}