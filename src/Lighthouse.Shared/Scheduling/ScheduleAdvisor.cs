namespace Lighthouse.Shared.Scheduling;

/// <summary>Завдання з нагадуванням; усі моменти в локальному часі користувача.</summary>
public sealed record ScheduleTask(Guid Id, DateTime ReminderLocal, DateTime? DeadlineLocal);

public sealed record RescheduleSuggestion(
    Guid TaskId, DateTime CurrentLocal, DateTime ProposedLocal, int CurrentBlock, int BestBlock);

/// <summary>
/// Порада «перенести нагадування в продуктивніший час». Блок — чотиригодинний інтервал доби. Нагадування в «малопродуктивному»
/// блоці (виконано не більше 25 % від кількості в найактивнішому за 90 діб; потрібно щонайменше 10 виконаних завдань)
/// пропонується перенести на 2 год після початку найактивнішого блоку; якщо це пізніше за дедлайн, поради немає.
/// </summary>
public static class ScheduleAdvisor
{
    public const int WindowDays = 90;
    public const int MinSample = 10;
    public const int LowShareDivisor = 4;

    public static IReadOnlyList<RescheduleSuggestion> Suggest(
        DateTime nowLocal, IEnumerable<DateTime> completedLocal, IEnumerable<ScheduleTask> openTasks)
    {
        var from = nowLocal.Date.AddDays(-(WindowDays - 1));
        var counts = new int[6];
        var sample = 0;
        foreach (var moment in completedLocal)
        {
            if (moment < from || moment > nowLocal)
            {
                continue;
            }

            counts[moment.Hour / 4]++;
            sample++;
        }

        if (sample < MinSample)
        {
            return [];
        }

        var best = Array.IndexOf(counts, counts.Max());
        if (counts[best] == 0)
        {
            return [];
        }

        var result = new List<RescheduleSuggestion>();
        foreach (var task in openTasks.OrderBy(t => t.ReminderLocal))
        {
            if (task.ReminderLocal <= nowLocal)
            {
                continue;
            }

            var block = task.ReminderLocal.Hour / 4;
            if (block == best || counts[block] * LowShareDivisor > counts[best])
            {
                continue;
            }

            var proposed = task.ReminderLocal.Date.AddHours(best * 4 + 2);
            if (proposed <= nowLocal)
            {
                proposed = proposed.AddDays(1);
            }

            if (task.DeadlineLocal is { } deadline && proposed >= deadline)
            {
                continue;
            }

            result.Add(new RescheduleSuggestion(task.Id, task.ReminderLocal, proposed, block, best));
        }

        return result;
    }
    
    /// <summary>
    /// Найпродуктивніший час доби для нагадування: середина найактивнішого чотиригодинного інтервалу за 90 діб
    /// (потрібно щонайменше 10 виконаних завдань), інакше null.
    /// </summary>
    public static TimeSpan? SuggestTime(DateTime nowLocal, IEnumerable<DateTime> completedLocal)
    {
        var from = nowLocal.Date.AddDays(-(WindowDays - 1));
        var counts = new int[6];
        var sample = 0;
        foreach (var moment in completedLocal)
        {
            if (moment >= from && moment <= nowLocal)
            {
                counts[moment.Hour / 4]++;
                sample++;
            }
        }

        if (sample < MinSample)
        {
            return null;
        }

        var best = Array.IndexOf(counts, counts.Max());
        return TimeSpan.FromHours(best * 4 + 2);
    }
}