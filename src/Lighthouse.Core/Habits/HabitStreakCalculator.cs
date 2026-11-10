using Lighthouse.Shared.Enums;

namespace Lighthouse.Core.Habits;

/// <summary>Поточна та найкраща серії звички.</summary>
public readonly record struct HabitStreaks(int Current, int Best);

/// <summary>
/// Обчислення серій звички з відміток (чиста функція, без залежностей — використовується і сервером, і клієнтом).
/// Правила: «період» — запланований день (Daily — кожен день; SpecificDays — дні за маскою, пн = біт 0)
/// або тиждень (TimesPerWeek, тиждень починається з понеділка; виконаний, якщо відміток ≥ TimesPerWeek).
/// Поточний період, який ще не виконано (сьогодні / поточний тиждень), серію не перериває.
/// Відмітки у незапланований день серію не переривають і не продовжують. Відмітки з майбутнім часом ігноруються.
/// </summary>
public static class HabitStreakCalculator
{
    private const int AllDaysMask = 0b1111111;

    public static HabitStreaks Calculate(
        HabitFrequencyType frequency,
        int daysOfWeekMask,
        int? timesPerWeek,
        IEnumerable<DateOnly> logDates,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(logDates);

        var dates = logDates.Where(d => d <= today).Distinct().OrderBy(d => d).ToList();
        if (dates.Count == 0)
        {
            return new HabitStreaks(0, 0);
        }

        var periods = frequency == HabitFrequencyType.TimesPerWeek
            ? WeeklyPeriods(dates, Math.Max(1, timesPerWeek ?? 1), today)
            : DailyPeriods(dates, frequency == HabitFrequencyType.SpecificDays ? daysOfWeekMask : AllDaysMask, today);

        var run = 0;
        var best = 0;
        foreach (var done in periods)
        {
            run = done ? run + 1 : 0;
            best = Math.Max(best, run);
        }

        return new HabitStreaks(run, best);
    }

    private static List<bool> DailyPeriods(List<DateOnly> dates, int mask, DateOnly today)
    {
        var logged = dates.ToHashSet();
        var result = new List<bool>();

        for (var day = dates[0]; day <= today; day = day.AddDays(1))
        {
            if (((mask >> DayIndex(day)) & 1) == 0)
            {
                continue; // незапланований день
            }

            var done = logged.Contains(day);
            if (day == today && !done)
            {
                continue; // сьогодні ще можна відмітити
            }

            result.Add(done);
        }

        return result;
    }

    private static List<bool> WeeklyPeriods(List<DateOnly> dates, int required, DateOnly today)
    {
        var counts = dates.GroupBy(WeekStart).ToDictionary(g => g.Key, g => g.Count());
        var currentWeek = WeekStart(today);
        var result = new List<bool>();

        for (var week = WeekStart(dates[0]); week <= currentWeek; week = week.AddDays(7))
        {
            counts.TryGetValue(week, out var count);
            var done = count >= required;
            if (week == currentWeek && !done)
            {
                continue; // поточний тиждень ще триває
            }

            result.Add(done);
        }

        return result;
    }

    /// <summary>Індекс дня тижня: понеділок = 0 … неділя = 6.</summary>
    private static int DayIndex(DateOnly date) => ((int)date.DayOfWeek + 6) % 7;

    private static DateOnly WeekStart(DateOnly date) => date.AddDays(-DayIndex(date));
}