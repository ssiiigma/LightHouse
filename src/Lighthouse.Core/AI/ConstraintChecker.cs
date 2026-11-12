namespace Lighthouse.Core.AI;

/// <summary>Завдання у вигляді, потрібному для перевірки обмежень (вже відсортоване за рангом).</summary>
public sealed record ConstraintTask(
    Guid Id, double Score, bool IsLayer1, DateTime? DeadlineUtc, int? EstimatedMinutes);

public sealed record ConstraintResult(
    int DailyLimit,
    IReadOnlyList<Guid> DeferralSuggestions,
    IReadOnlyList<Guid> DoNotFit);

/// <summary>
/// Обмеження: денний ліміт L = max(3, ceil(1.5·M)) (M — медіана виконаних за добу; новому користувачу L = 5),
/// пропозиції перенести зайві завдання на наступну добу та виявлення завдань, що не вкладаються в час до дедлайну.
/// </summary>
public static class ConstraintChecker
{
    public const int NewUserLimit = 5;
    public const int MinimumLimit = 3;
    public const double LimitFactor = 1.5;

    public static int DailyLimit(double? medianCompletedPerDay) =>
        medianCompletedPerDay is { } median
            ? Math.Max(MinimumLimit, (int)Math.Ceiling(LimitFactor * median))
            : NewUserLimit;

    public static ConstraintResult Evaluate(
        IReadOnlyList<ConstraintTask> openTasks, int dailyLimit, DateTime nowUtc, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(openTasks);
        ArgumentNullException.ThrowIfNull(zone);

        var now = ZoneTime.ToUtc(nowUtc);
        return new ConstraintResult(
            dailyLimit,
            SuggestDeferrals(openTasks, dailyLimit, now, zone),
            FindTasksThatDoNotFit(openTasks, now));
    }

    /// <summary>
    /// Якщо відкритих завдань більше за ліміт, пропонується перенести надлишок: найменш пріоритетні (за оцінкою)
    /// завдання не з шару 1, дедлайн яких (якщо він є) пізніший за кінець наступної доби.
    /// </summary>
    private static List<Guid> SuggestDeferrals(
        IReadOnlyList<ConstraintTask> tasks, int dailyLimit, DateTime now, TimeZoneInfo zone)
    {
        var excess = tasks.Count - dailyLimit;
        if (excess <= 0)
        {
            return [];
        }

        var localToday = TimeZoneInfo.ConvertTimeFromUtc(now, zone).Date;
        var afterNextDay = ZoneTime.LocalMidnightToUtc(localToday.AddDays(2), zone);

        return tasks
            .Where(t => !t.IsLayer1 && (t.DeadlineUtc is not { } d || ZoneTime.ToUtc(d) >= afterNextDay))
            .OrderBy(t => t.Score)
            .ThenBy(t => t.Id)
            .Take(excess)
            .Select(t => t.Id)
            .ToList();
    }

    /// <summary>
    /// Завдання з дедлайном і оцінкою розглядаються в порядку дедлайнів; воно «не вкладається», якщо сумарна
    /// оцінка його та всіх попередніх перевищує час, що лишився до його дедлайну. Прострочені пропускаються.
    /// </summary>
    private static List<Guid> FindTasksThatDoNotFit(IReadOnlyList<ConstraintTask> tasks, DateTime now)
    {
        var result = new List<Guid>();
        var cumulativeMinutes = 0.0;

        var candidates = tasks
            .Where(t => t.DeadlineUtc is not null && t.EstimatedMinutes is > 0)
            .Select(t => (Task: t, Deadline: ZoneTime.ToUtc(t.DeadlineUtc!.Value)))
            .Where(item => item.Deadline > now)
            .OrderBy(item => item.Deadline)
            .ThenBy(item => item.Task.Id);

        foreach (var (task, deadline) in candidates)
        {
            cumulativeMinutes += task.EstimatedMinutes!.Value;
            if (cumulativeMinutes > (deadline - now).TotalMinutes)
            {
                result.Add(task.Id);
            }
        }

        return result;
    }
}