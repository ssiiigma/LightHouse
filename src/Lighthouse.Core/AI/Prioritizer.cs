using Lighthouse.Core.Entities;
using Lighthouse.Shared.Enums;

namespace Lighthouse.Core.AI;

public enum RankingMode
{
    Heuristic,
    LinUcb
}

/// <param name="OpenTasks">Завдання користувача (виконані й видалені ігноруються).</param>
/// <param name="Model">Модель користувача; null — застосовується евристика.</param>
/// <param name="FeedbackCount">Кількість подій відгуку; LinUCB діє, коли їх ≥ N_min.</param>
public sealed record PrioritizationRequest(
    IReadOnlyList<TaskItem> OpenTasks,
    UserHistory History,
    LinUcbModel? Model,
    int FeedbackCount,
    DateTime NowUtc,
    TimeZoneInfo Zone);

/// <param name="Position">Місце в рейтингу, з 1.</param>
/// <param name="Context">Вектор ознак на момент показу (зберігається у ContextJson для перших K).</param>
public sealed record RankedTask(
    Guid TaskId, int Position, double Score, bool IsLayer1, double[] Context, Explanation Explanation);

public sealed record PrioritizationResult(
    RankingMode Mode,
    IReadOnlyList<RankedTask> Ranked,
    int DailyLimit,
    IReadOnlyList<Guid> DeferralSuggestions,
    IReadOnlyList<Guid> DoNotFit);

/// <summary>Модуль AI викликається через цей інтерфейс (архітектура: Recommendations → IPrioritizer).</summary>
public interface IPrioritizer
{
    PrioritizationResult Rank(PrioritizationRequest request);
}

/// <summary>
/// Двошарове ранжування: шар 1 — прострочені та Critical із дедлайном ≤ 24 год; у межах шару і всередині
/// шарів сортування за оцінкою (LinUCB або евристика), шар 1 завжди вище.
/// </summary>
public sealed class Prioritizer : IPrioritizer
{
    private static readonly TimeSpan Layer1Window = TimeSpan.FromHours(24);

    public PrioritizationResult Rank(PrioritizationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = ZoneTime.ToUtc(request.NowUtc);
        var model = request.Model;
        if (model is not null && model.Dimension != LinUcbDefaults.Dimension)
        {
            throw new ArgumentException(
                $"Model dimension {model.Dimension} does not match {LinUcbDefaults.Dimension}.", nameof(request));
        }

        var useModel = model is not null && request.FeedbackCount >= LinUcbDefaults.MinFeedbackEvents;
        var theta = useModel ? model!.Theta() : null;

        var items = new List<(TaskItem Task, double Score, bool Layer1, double[] X, Explanation Explanation)>();
        foreach (var task in request.OpenTasks)
        {
            if (task.IsDeleted || task.CompletedAtUtc is not null)
            {
                continue;
            }

            var x = TaskFeatureExtractor.Extract(task, request.History, now, request.Zone);

            double score, bonus, expected;
            double[] contributions;
            if (useModel)
            {
                contributions = new double[x.Length];
                expected = 0.0;
                for (var j = 0; j < x.Length; j++)
                {
                    contributions[j] = theta![j] * x[j];
                    expected += contributions[j];
                }

                bonus = model!.Bonus(x);
                score = expected + bonus;
            }
            else
            {
                contributions = HeuristicScorer.Contributions(x);
                expected = HeuristicScorer.Score(x);
                bonus = 0.0;
                score = expected;
            }

            items.Add((task, score, IsLayer1(task, now), x,
                ExplanationBuilder.Build(contributions, x, bonus, expected)));
        }

        var ordered = items
            .OrderByDescending(i => i.Layer1)
            .ThenByDescending(i => i.Score)
            .ThenBy(i => i.Task.DeadlineUtc ?? DateTime.MaxValue)
            .ThenBy(i => i.Task.Id)
            .ToList();

        var ranked = ordered
            .Select((i, index) => new RankedTask(i.Task.Id, index + 1, i.Score, i.Layer1, i.X, i.Explanation))
            .ToList();

        var limit = ConstraintChecker.DailyLimit(request.History.MedianCompletedPerDay);
        var constraints = ConstraintChecker.Evaluate(
            ordered.Select(i => new ConstraintTask(
                i.Task.Id, i.Score, i.Layer1, i.Task.DeadlineUtc, i.Task.EstimatedMinutes)).ToList(),
            limit, now, request.Zone);

        return new PrioritizationResult(
            useModel ? RankingMode.LinUcb : RankingMode.Heuristic,
            ranked,
            constraints.DailyLimit,
            constraints.DeferralSuggestions,
            constraints.DoNotFit);
    }

    private static bool IsLayer1(TaskItem task, DateTime now)
    {
        if (task.DeadlineUtc is not { } rawDeadline)
        {
            return false;
        }

        var deadline = ZoneTime.ToUtc(rawDeadline);
        return deadline < now || (task.Priority == TaskPriority.Critical && deadline - now <= Layer1Window);
    }
}