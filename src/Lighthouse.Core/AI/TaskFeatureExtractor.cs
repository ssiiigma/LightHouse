using Lighthouse.Core.Entities;
using Lighthouse.Shared.Enums;

namespace Lighthouse.Core.AI;

/// <summary>
/// Побудова вектора ознак x (d = 10, усі компоненти в [0; 1]):
/// x1 = 1; x2 пріоритет; x3 терміновість 1/(1 + h/24); x4 ознака прострочення; x5 min(оцінка/240, 1);
/// x6–x8 згладжені частки виконаних (інтервал доби, день тижня, категорія); x9 min(відкладання/5, 1); x10 нагадування.
/// </summary>
public static class TaskFeatureExtractor
{
    public static double[] Extract(TaskItem task, UserHistory history, DateTime nowUtc, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(zone);

        var now = ZoneTime.ToUtc(nowUtc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(now, zone);
        DateTime? deadline = task.DeadlineUtc is { } rawDeadline ? ZoneTime.ToUtc(rawDeadline) : null;

        var x = new double[LinUcbDefaults.Dimension];
        x[FeatureIndex.Bias] = 1.0;
        x[FeatureIndex.Priority] = PriorityValue(task.Priority);
        x[FeatureIndex.Urgency] = Urgency(deadline, now);
        x[FeatureIndex.Overdue] = deadline is { } due && due < now ? 1.0 : 0.0;
        x[FeatureIndex.Estimate] = task.EstimatedMinutes is { } minutes && minutes > 0
            ? Math.Min(minutes / 240.0, 1.0)
            : 0.0;
        x[FeatureIndex.HourBucketRate] = Rate(history.ByHourBucket, local.Hour / 4);
        x[FeatureIndex.WeekDayRate] = Rate(history.ByWeekDay, ((int)local.DayOfWeek + 6) % 7);
        x[FeatureIndex.CategoryRate] =
            task.CategoryId is { } categoryId && history.ByCategory.TryGetValue(categoryId, out var categoryCount)
                ? categoryCount.SmoothedRate
                : default(OutcomeCount).SmoothedRate;
        x[FeatureIndex.Postpone] =
            history.PostponeCounts.TryGetValue(task.Id, out var postponed) ? Math.Min(postponed / 5.0, 1.0) : 0.0;
        x[FeatureIndex.Reminder] = task.ReminderAtUtc is not null ? 1.0 : 0.0;
        return x;
    }

    private static double PriorityValue(TaskPriority priority) => priority switch
    {
        TaskPriority.Low => 0.0,
        TaskPriority.Medium => 0.33,
        TaskPriority.High => 0.67,
        TaskPriority.Critical => 1.0,
        _ => 0.33
    };

    /// <summary>Без дедлайну = 0; прострочене (h ≤ 0) = 1; інакше 1/(1 + h/24).</summary>
    private static double Urgency(DateTime? deadlineUtc, DateTime nowUtc)
    {
        if (deadlineUtc is not { } deadline)
        {
            return 0.0;
        }

        var hours = (deadline - nowUtc).TotalHours;
        return hours <= 0.0 ? 1.0 : 1.0 / (1.0 + hours / 24.0);
    }

    private static double Rate(IReadOnlyList<OutcomeCount> counts, int index) =>
        index < counts.Count ? counts[index].SmoothedRate : default(OutcomeCount).SmoothedRate;
}