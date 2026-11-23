using Lighthouse.Shared.Analytics;
using Lighthouse.Shared.Enums;

namespace Lighthouse.Shared.Notifications;

/// <summary>Налаштування локальних нагадувань (зберігаються на пристрої; синхронізація із сервером — у P5.4).</summary>
public sealed record NotificationPrefs(
    bool TaskReminders, bool HabitReminders, bool QuietEnabled, TimeSpan QuietStart, TimeSpan QuietEnd)
{
    public static NotificationPrefs Default { get; } =
        new(true, true, false, new TimeSpan(22, 0, 0), new TimeSpan(7, 0, 0));
}

public sealed record PlanTask(Guid Id, DateTime? ReminderLocal, bool IsCompleted);

public sealed record PlanHabit(
    Guid Id, HabitFrequencyType Frequency, int DaysOfWeekMask, int? TimesPerWeek,
    int? ReminderMinutes, IReadOnlyList<DateOnly> Logs);

public sealed record PlannedNotification(int Id, NotificationKind Kind, Guid TargetId, DateTime AtLocal);

/// <summary>Тихий режим: сповіщення, що потрапляє у вікно, переноситься на його кінець (вікно може переходити через північ).</summary>
public static class QuietHours
{
    public static DateTime Shift(DateTime local, NotificationPrefs prefs)
    {
        if (!prefs.QuietEnabled || prefs.QuietStart == prefs.QuietEnd)
        {
            return local;
        }

        var time = local.TimeOfDay;
        var (start, end) = (prefs.QuietStart, prefs.QuietEnd);

        if (start < end)
        {
            return time >= start && time < end ? local.Date + end : local;
        }

        if (time >= start)
        {
            return local.Date.AddDays(1) + end;
        }

        return time < end ? local.Date + end : local;
    }
}

public static class NotificationIds
{
    /// <summary>Стабільний додатний ідентифікатор сповіщення: той самий елемент і слот завжди дають той самий номер.</summary>
    public static int For(NotificationKind kind, Guid target, int slot)
    {
        unchecked
        {
            var hash = BitConverter.ToInt32(target.ToByteArray(), 0);
            hash = hash * 31 + (int)kind * 104729 + slot * 7919;
            hash &= 0x7FFFFFFF;
            return hash == 0 ? 1 : hash;
        }
    }
}

/// <summary>
/// План локальних сповіщень: нагадування завдань (на заданий час) і звичок (ковзне вікно 14 днів з урахуванням днів тижня,
/// виконаних відміток і тижневої норми «N разів на тиждень»). Результат впорядкований за часом і обмежений 400 записами.
/// </summary>
public static class NotificationPlanner
{
    public const int HabitWindowDays = 14;
    public const int MaxScheduled = 400;

    public static IReadOnlyList<PlannedNotification> Plan(
        DateTime nowLocal, NotificationPrefs prefs, IEnumerable<PlanTask> tasks, IEnumerable<PlanHabit> habits)
    {
        var result = new List<PlannedNotification>();

        if (prefs.TaskReminders)
        {
            foreach (var task in tasks)
            {
                if (task.IsCompleted || task.ReminderLocal is not { } at || at <= nowLocal)
                {
                    continue;
                }

                result.Add(new PlannedNotification(
                    NotificationIds.For(NotificationKind.TaskReminder, task.Id, 0),
                    NotificationKind.TaskReminder, task.Id, QuietHours.Shift(at, prefs)));
            }
        }

        if (prefs.HabitReminders)
        {
            var today = DateOnly.FromDateTime(nowLocal);
            foreach (var habit in habits)
            {
                if (habit.ReminderMinutes is not { } minutes)
                {
                    continue;
                }

                var logs = habit.Logs.ToHashSet();
                var weeklyTarget = Math.Clamp(habit.TimesPerWeek ?? 1, 1, 7);

                for (var offset = 0; offset < HabitWindowDays; offset++)
                {
                    var date = today.AddDays(offset);
                    var at = date.ToDateTime(TimeOnly.MinValue).AddMinutes(minutes);
                    if (at <= nowLocal || logs.Contains(date))
                    {
                        continue;
                    }

                    if (habit.Frequency == HabitFrequencyType.SpecificDays
                        && ((habit.DaysOfWeekMask >> AnalyticsCalculator.DayIndex(date)) & 1) == 0)
                    {
                        continue;
                    }

                    if (habit.Frequency == HabitFrequencyType.TimesPerWeek && WeekCount(logs, date) >= weeklyTarget)
                    {
                        continue;
                    }

                    result.Add(new PlannedNotification(
                        NotificationIds.For(NotificationKind.HabitReminder, habit.Id, date.DayNumber),
                        NotificationKind.HabitReminder, habit.Id, QuietHours.Shift(at, prefs)));
                }
            }
        }

        return result.OrderBy(p => p.AtLocal).ThenBy(p => p.Id).Take(MaxScheduled).ToList();
    }

    private static int WeekCount(HashSet<DateOnly> logs, DateOnly date)
    {
        var start = date.AddDays(-AnalyticsCalculator.DayIndex(date));
        return Enumerable.Range(0, 7).Count(i => logs.Contains(start.AddDays(i)));
    }
    
    /// <param name="Sent">Скільки пристроїв прийняли тестове сповіщення.</param>
    public sealed record PushTestResultDto(int Sent);
}