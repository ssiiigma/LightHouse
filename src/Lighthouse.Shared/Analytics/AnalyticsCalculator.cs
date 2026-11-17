using Lighthouse.Shared.Enums;
using Lighthouse.Shared.Habits;

namespace Lighthouse.Shared.Analytics;

public enum AnalyticsPeriod
{
    Day = 0,
    Week = 1,
    Month = 2,
    Year = 3
}

/// <summary>Одиниця стовпчиків діаграми «завдання за днями».</summary>
public enum BucketKind
{
    /// <summary>Чотиригодинні інтервали доби (вкладка «День»).</summary>
    HourBlock,
    Day,
    Month
}

/// <summary>Завдання для аналітики; усі моменти — у локальному часі користувача.</summary>
public sealed record AnalyticsTask(Guid? CategoryId, DateTime? DeadlineLocal, DateTime? CompletedLocal);

public sealed record AnalyticsHabit(
    HabitFrequencyType Frequency, int DaysOfWeekMask, int? TimesPerWeek, IReadOnlyList<DateOnly> Logs);

public readonly record struct DateRange(DateOnly From, DateOnly To)
{
    public int Days => To.DayNumber - From.DayNumber + 1;

    public bool Contains(DateOnly date) => date >= From && date <= To;
}

public sealed record CategoryShare(Guid? CategoryId, int Count, double Fraction);

public sealed record AnalyticsResult(
    DateRange Range,
    DateRange PreviousRange,
    int Completed,
    int Planned,
    double? CompletionRate,
    int PreviousCompleted,
    double? PreviousCompletionRate,
    double? HabitRegularity,
    double? PreviousHabitRegularity,
    int CurrentStreak,
    int BestStreak,
    BucketKind BucketKind,
    int[] Buckets,
    int CurrentBucket,
    IReadOnlyList<CategoryShare> Categories,
    DayOfWeek? ProductiveDay,
    int? ProductiveHourBlock,
    int InsightSample,
    DateOnly HeatmapStart,
    int[] HeatmapCounts);

/// <summary>
/// Аналітика на пристрої за даними SQLite (працює офлайн). Чиста функція без залежностей від платформи.
/// Визначення: «заплановане» = завдання, виконані в періоді, плюс невиконані з дедлайном у періоді;
/// відсоток виконання = виконані / заплановані. Регулярність звичок = відмітки / очікувані відмітки від першої відмітки
/// звички (або початку періоду) до сьогодні. Теплова карта й найпродуктивніший час не залежать від вкладки періоду.
/// </summary>
public static class AnalyticsCalculator
{
    public const int HeatmapWeeks = 17;
    public const int InsightWindowDays = 90;
    public const int InsightMinSample = 5;

    public static AnalyticsResult Calculate(
        AnalyticsPeriod period, DateTime nowLocal, IReadOnlyList<AnalyticsTask> tasks, IReadOnlyList<AnalyticsHabit> habits)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(habits);

        var today = DateOnly.FromDateTime(nowLocal);
        var range = RangeFor(period, today);
        var previous = PreviousRange(period, range);

        var (completed, planned) = Completion(tasks, range);
        var (previousCompleted, previousPlanned) = Completion(tasks, previous);

        var streaks = habits
            .Select(h => HabitStreakCalculator.Calculate(h.Frequency, h.DaysOfWeekMask, h.TimesPerWeek, h.Logs, today))
            .ToList();

        var (kind, buckets, currentBucket) = Buckets(period, range, nowLocal, tasks);
        var (day, block, sample) = Productivity(tasks, today);
        var (heatStart, heat) = Heatmap(tasks, habits, today);

        return new AnalyticsResult(
            range, previous,
            completed, planned, Rate(completed, planned),
            previousCompleted, Rate(previousCompleted, previousPlanned),
            HabitScore(habits, range, today), HabitScore(habits, previous, today),
            streaks.Count == 0 ? 0 : streaks.Max(s => s.Current),
            streaks.Count == 0 ? 0 : streaks.Max(s => s.Best),
            kind, buckets, currentBucket,
            Categories(tasks, range, completed),
            day, block, sample,
            heatStart, heat);
    }

    public static DateRange RangeFor(AnalyticsPeriod period, DateOnly anchor) => period switch
    {
        AnalyticsPeriod.Day => new DateRange(anchor, anchor),
        AnalyticsPeriod.Week => new DateRange(anchor.AddDays(-DayIndex(anchor)), anchor.AddDays(6 - DayIndex(anchor))),
        AnalyticsPeriod.Month => new DateRange(
            new DateOnly(anchor.Year, anchor.Month, 1),
            new DateOnly(anchor.Year, anchor.Month, DateTime.DaysInMonth(anchor.Year, anchor.Month))),
        _ => new DateRange(new DateOnly(anchor.Year, 1, 1), new DateOnly(anchor.Year, 12, 31))
    };

    public static DateRange PreviousRange(AnalyticsPeriod period, DateRange current)
    {
        switch (period)
        {
            case AnalyticsPeriod.Day:
                return new DateRange(current.From.AddDays(-1), current.From.AddDays(-1));
            case AnalyticsPeriod.Week:
                return new DateRange(current.From.AddDays(-7), current.From.AddDays(-1));
            case AnalyticsPeriod.Month:
            {
                var from = current.From.AddMonths(-1);
                return new DateRange(from, current.From.AddDays(-1));
            }
            default:
                return new DateRange(current.From.AddYears(-1), current.From.AddDays(-1));
        }
    }

    /// <summary>Понеділок = 0 … неділя = 6.</summary>
    public static int DayIndex(DateOnly date) => ((int)date.DayOfWeek + 6) % 7;

    private static double? Rate(int completed, int planned) => planned == 0 ? null : (double)completed / planned;

    private static bool InRange(DateTime? moment, DateRange range) =>
        moment is { } m && range.Contains(DateOnly.FromDateTime(m));

    private static (int Completed, int Planned) Completion(IReadOnlyList<AnalyticsTask> tasks, DateRange range)
    {
        int completed = 0, open = 0;
        foreach (var task in tasks)
        {
            if (InRange(task.CompletedLocal, range))
            {
                completed++;
            }
            else if (task.CompletedLocal is null && InRange(task.DeadlineLocal, range))
            {
                open++;
            }
        }

        return (completed, completed + open);
    }

    private static double? HabitScore(IReadOnlyList<AnalyticsHabit> habits, DateRange range, DateOnly today)
    {
        int done = 0, expected = 0;
        foreach (var habit in habits)
        {
            if (habit.Logs.Count == 0)
            {
                continue; // без відміток немає опорної дати початку
            }

            var first = habit.Logs.Min();
            var from = first > range.From ? first : range.From;
            var to = today < range.To ? today : range.To;
            if (from > to)
            {
                continue;
            }

            var days = to.DayNumber - from.DayNumber + 1;
            var expectedForHabit = habit.Frequency switch
            {
                HabitFrequencyType.Daily => days,
                HabitFrequencyType.SpecificDays => Enumerable.Range(0, days)
                    .Count(i => ((habit.DaysOfWeekMask >> DayIndex(from.AddDays(i))) & 1) == 1),
                _ => Math.Max(1, (int)Math.Round(Math.Clamp(habit.TimesPerWeek ?? 1, 1, 7) * days / 7.0))
            };
            if (expectedForHabit == 0)
            {
                continue;
            }

            var logs = habit.Logs.Distinct().Count(d => d >= from && d <= to);
            expected += expectedForHabit;
            done += Math.Min(logs, expectedForHabit);
        }

        return expected == 0 ? null : (double)done / expected;
    }

    private static (BucketKind Kind, int[] Values, int Current) Buckets(
        AnalyticsPeriod period, DateRange range, DateTime now, IReadOnlyList<AnalyticsTask> tasks)
    {
        var completed = tasks.Where(t => InRange(t.CompletedLocal, range)).Select(t => t.CompletedLocal!.Value).ToList();

        switch (period)
        {
            case AnalyticsPeriod.Day:
            {
                var values = new int[6];
                foreach (var moment in completed)
                {
                    values[moment.Hour / 4]++;
                }

                return (BucketKind.HourBlock, values, now.Hour / 4);
            }
            case AnalyticsPeriod.Week:
            case AnalyticsPeriod.Month:
            {
                var values = new int[range.Days];
                foreach (var moment in completed)
                {
                    values[DateOnly.FromDateTime(moment).DayNumber - range.From.DayNumber]++;
                }

                return (BucketKind.Day, values, DateOnly.FromDateTime(now).DayNumber - range.From.DayNumber);
            }
            default:
            {
                var values = new int[12];
                foreach (var moment in completed)
                {
                    values[moment.Month - 1]++;
                }

                return (BucketKind.Month, values, now.Month - 1);
            }
        }
    }

    private static IReadOnlyList<CategoryShare> Categories(IReadOnlyList<AnalyticsTask> tasks, DateRange range, int completed)
    {
        if (completed == 0)
        {
            return [];
        }

        return tasks
            .Where(t => InRange(t.CompletedLocal, range))
            .GroupBy(t => t.CategoryId)
            .Select(g => new CategoryShare(g.Key, g.Count(), (double)g.Count() / completed))
            .OrderByDescending(c => c.Count)
            .ToList();
    }

    /// <summary>Найпродуктивніший день тижня й чотиригодинний інтервал за останні 90 діб; null, якщо виконаних менше 5.</summary>
    private static (DayOfWeek? Day, int? Block, int Sample) Productivity(IReadOnlyList<AnalyticsTask> tasks, DateOnly today)
    {
        var window = new DateRange(today.AddDays(-(InsightWindowDays - 1)), today);
        var sample = tasks
            .Where(t => InRange(t.CompletedLocal, window))
            .Select(t => t.CompletedLocal!.Value)
            .ToList();

        if (sample.Count < InsightMinSample)
        {
            return (null, null, sample.Count);
        }

        var weekdays = new int[7];
        var blocks = new int[6];
        foreach (var moment in sample)
        {
            weekdays[DayIndex(DateOnly.FromDateTime(moment))]++;
            blocks[moment.Hour / 4]++;
        }

        var bestDay = Array.IndexOf(weekdays, weekdays.Max());
        var bestBlock = Array.IndexOf(blocks, blocks.Max());
        return ((DayOfWeek)((bestDay + 1) % 7), bestBlock, sample.Count);
    }

    /// <summary>Активність за 17 тижнів: виконані завдання + відмітки звичок за день; індекс = дні від понеділка першого тижня; майбутнє = −1.</summary>
    private static (DateOnly Start, int[] Counts) Heatmap(
        IReadOnlyList<AnalyticsTask> tasks, IReadOnlyList<AnalyticsHabit> habits, DateOnly today)
    {
        var start = today.AddDays(-DayIndex(today) - 7 * (HeatmapWeeks - 1));
        var counts = new int[HeatmapWeeks * 7];

        void Add(DateOnly date)
        {
            var offset = date.DayNumber - start.DayNumber;
            if (offset >= 0 && offset < counts.Length)
            {
                counts[offset]++;
            }
        }

        foreach (var task in tasks.Where(t => t.CompletedLocal is not null))
        {
            Add(DateOnly.FromDateTime(task.CompletedLocal!.Value));
        }

        foreach (var habit in habits)
        {
            foreach (var date in habit.Logs.Distinct())
            {
                Add(date);
            }
        }

        for (var i = 0; i < counts.Length; i++)
        {
            if (start.AddDays(i) > today)
            {
                counts[i] = -1;
            }
        }

        return (start, counts);
    }
    
    /// <summary>Підсумок за діапазон: виконання, регулярність звичок, серії, виконані за днями й розподіл за категоріями.</summary>
    public static RangeSummary Summarize(
        DateRange range, DateTime nowLocal, IReadOnlyList<AnalyticsTask> tasks, IReadOnlyList<AnalyticsHabit> habits)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(habits);

        var today = DateOnly.FromDateTime(nowLocal);
        var (completed, planned) = Completion(tasks, range);
        var streaks = habits
            .Select(h => HabitStreakCalculator.Calculate(h.Frequency, h.DaysOfWeekMask, h.TimesPerWeek, h.Logs, today))
            .ToList();

        var perDay = new int[range.Days];
        foreach (var task in tasks)
        {
            if (InRange(task.CompletedLocal, range))
            {
                perDay[DateOnly.FromDateTime(task.CompletedLocal!.Value).DayNumber - range.From.DayNumber]++;
            }
        }

        return new RangeSummary(
            range, completed, planned, Rate(completed, planned), HabitScore(habits, range, today),
            streaks.Count == 0 ? 0 : streaks.Max(s => s.Current),
            streaks.Count == 0 ? 0 : streaks.Max(s => s.Best),
            perDay.Select((count, index) => new DayCount(range.From.AddDays(index), count)).ToList(),
            Categories(tasks, range, completed));
    }
}

public sealed record DayCount(DateOnly Date, int Completed);

/// <summary>Підсумок за довільний діапазон дат (для звітів); визначення показників збігаються з аналітикою застосунку.</summary>
public sealed record RangeSummary(
    DateRange Range,
    int Completed,
    int Planned,
    double? CompletionRate,
    double? HabitRegularity,
    int CurrentStreak,
    int BestStreak,
    IReadOnlyList<DayCount> Days,
    IReadOnlyList<CategoryShare> Categories);