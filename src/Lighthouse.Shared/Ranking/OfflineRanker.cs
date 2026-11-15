using Lighthouse.Shared.Enums;

namespace Lighthouse.Shared.Ranking;

public sealed record OfflineTask(Guid Id, TaskPriority Priority, DateTime? DeadlineUtc);

public sealed record OfflineRankedTask(
    Guid TaskId, int Position, double Score, bool IsLayer1, IReadOnlyList<string> ReasonCodes);

/// <summary>
/// Офлайн-ранжування на пристрої: евристика h = 0.45·x2 + 0.45·x3 + 0.10·x4 і два шари (прострочені та Critical із дедлайном ≤ 24 год
/// завжди вище). Збігається з серверним холодним стартом; паритет перевіряється тестом.
/// </summary>
public static class OfflineRanker
{
    private static readonly TimeSpan Layer1Window = TimeSpan.FromHours(24);

    public static IReadOnlyList<OfflineRankedTask> Rank(IEnumerable<OfflineTask> tasks, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        var now = Normalize(nowUtc);

        var items = tasks.Select(task =>
        {
            DateTime? deadline = task.DeadlineUtc is { } raw ? Normalize(raw) : null;

            var x = new double[10];
            x[FeatureIndex.Bias] = 1.0;
            x[FeatureIndex.Priority] = PriorityValue(task.Priority);
            x[FeatureIndex.Urgency] = Urgency(deadline, now);
            x[FeatureIndex.Overdue] = deadline is { } due && due < now ? 1.0 : 0.0;

            var score = HeuristicScorer.Score(x);
            var explanation = ExplanationBuilder.Build(HeuristicScorer.Contributions(x), x, 0.0, score);
            var layer1 = deadline is { } d && (d < now || (task.Priority == TaskPriority.Critical && d - now <= Layer1Window));

            return (Task: task, Deadline: deadline, Score: score, Layer1: layer1,
                Codes: (IReadOnlyList<string>)explanation.Reasons.Select(r => r.Code).ToList());
        })
        .OrderByDescending(i => i.Layer1)
        .ThenByDescending(i => i.Score)
        .ThenBy(i => i.Deadline ?? DateTime.MaxValue)
        .ThenBy(i => i.Task.Id)
        .ToList();

        return items
            .Select((i, index) => new OfflineRankedTask(i.Task.Id, index + 1, i.Score, i.Layer1, i.Codes))
            .ToList();
    }

    private static double PriorityValue(TaskPriority priority) => priority switch
    {
        TaskPriority.Low => 0.0,
        TaskPriority.Medium => 0.33,
        TaskPriority.High => 0.67,
        TaskPriority.Critical => 1.0,
        _ => 0.33
    };

    private static double Urgency(DateTime? deadline, DateTime now)
    {
        if (deadline is not { } due)
        {
            return 0.0;
        }

        var hours = (due - now).TotalHours;
        return hours <= 0.0 ? 1.0 : 1.0 / (1.0 + hours / 24.0);
    }

    private static DateTime Normalize(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}