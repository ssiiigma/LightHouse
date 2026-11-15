using Lighthouse.Shared.Analytics;
using Lighthouse.Shared.Enums;
using Xunit;

namespace Lighthouse.Core.Tests;

public class AnalyticsCalculatorTests
{
    // Вівторок, 10 листопада 2026 р.
    private static readonly DateTime Now = new(2026, 11, 10, 12, 0, 0);

    private static DateTime D(int month, int day, int hour = 12) => new(2026, month, day, hour, 0, 0);

    private static AnalyticsResult Calc(
        AnalyticsPeriod period, IReadOnlyList<AnalyticsTask>? tasks = null, IReadOnlyList<AnalyticsHabit>? habits = null) =>
        AnalyticsCalculator.Calculate(period, Now, tasks ?? [], habits ?? []);

    [Fact]
    public void Ranges_AreCalendarAligned()
    {
        var week = AnalyticsCalculator.RangeFor(AnalyticsPeriod.Week, new DateOnly(2026, 11, 10));
        Assert.Equal(new DateRange(new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 15)), week);

        var month = AnalyticsCalculator.RangeFor(AnalyticsPeriod.Month, new DateOnly(2026, 11, 10));
        Assert.Equal(new DateRange(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30)), month);

        var year = AnalyticsCalculator.RangeFor(AnalyticsPeriod.Year, new DateOnly(2026, 11, 10));
        Assert.Equal(365, year.Days);
    }

    [Fact]
    public void PreviousRange_ForMonthAndWeek()
    {
        var month = AnalyticsCalculator.RangeFor(AnalyticsPeriod.Month, new DateOnly(2026, 3, 5));
        Assert.Equal(new DateRange(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28)),
            AnalyticsCalculator.PreviousRange(AnalyticsPeriod.Month, month));

        var week = AnalyticsCalculator.RangeFor(AnalyticsPeriod.Week, new DateOnly(2026, 11, 10));
        Assert.Equal(new DateRange(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 8)),
            AnalyticsCalculator.PreviousRange(AnalyticsPeriod.Week, week));
    }

    [Fact]
    public void CompletionRate_CountsCompletedPlusOpenDueInPeriod_AndIgnoresUndatedOpenTasks()
    {
        AnalyticsTask[] tasks =
        [
            new(null, null, D(11, 10)),          // виконане цього тижня
            new(null, null, D(11, 2)),           // виконане минулого тижня
            new(null, D(11, 12), null),          // відкрите з дедлайном цього тижня
            new(null, null, null),               // відкрите без дедлайну: не планується
            new(null, D(11, 20), null)           // дедлайн поза тижнем
        ];

        var result = Calc(AnalyticsPeriod.Week, tasks);

        Assert.Equal(1, result.Completed);
        Assert.Equal(2, result.Planned);
        Assert.Equal(0.5, result.CompletionRate);
        Assert.Equal(1, result.PreviousCompleted);
        Assert.Equal(1.0, result.PreviousCompletionRate);
    }

    [Fact]
    public void EmptyData_HasNoRatesAndZeroBuckets()
    {
        var result = Calc(AnalyticsPeriod.Week);

        Assert.Null(result.CompletionRate);
        Assert.Null(result.HabitRegularity);
        Assert.Equal(0, result.CurrentStreak);
        Assert.Equal(new int[7], result.Buckets);
        Assert.Empty(result.Categories);
        Assert.Null(result.ProductiveDay);
    }

    [Fact]
    public void Buckets_ByPeriod()
    {
        AnalyticsTask[] tasks =
        [
            new(null, null, D(11, 9, 8)),
            new(null, null, D(11, 10, 9)),
            new(null, null, D(11, 10, 15)),
            new(null, null, D(10, 3, 10))
        ];

        var week = Calc(AnalyticsPeriod.Week, tasks);
        Assert.Equal(BucketKind.Day, week.BucketKind);
        Assert.Equal(new[] { 1, 2, 0, 0, 0, 0, 0 }, week.Buckets);
        Assert.Equal(1, week.CurrentBucket);

        var day = Calc(AnalyticsPeriod.Day, tasks);
        Assert.Equal(BucketKind.HourBlock, day.BucketKind);
        Assert.Equal(new[] { 0, 0, 1, 1, 0, 0 }, day.Buckets);
        Assert.Equal(3, day.CurrentBucket);

        var year = Calc(AnalyticsPeriod.Year, tasks);
        Assert.Equal(12, year.Buckets.Length);
        Assert.Equal(3, year.Buckets[10]); // листопад
        Assert.Equal(1, year.Buckets[9]);  // жовтень
        Assert.Equal(10, year.CurrentBucket);
    }

    [Fact]
    public void Categories_AreSortedAndSumToOne()
    {
        var study = Guid.NewGuid();
        AnalyticsTask[] tasks =
        [
            new(study, null, D(11, 9)),
            new(study, null, D(11, 10)),
            new(study, null, D(11, 10)),
            new(null, null, D(11, 10))
        ];

        var result = Calc(AnalyticsPeriod.Week, tasks);

        Assert.Equal(2, result.Categories.Count);
        Assert.Equal(study, result.Categories[0].CategoryId);
        Assert.Equal(0.75, result.Categories[0].Fraction);
        Assert.Equal(1.0, result.Categories.Sum(c => c.Fraction), precision: 10);
    }

    [Fact]
    public void Productivity_NeedsMinimumSample_AndFindsBestWeekdayAndBlock()
    {
        // П'ять виконаних за вівторками о 10:30 (інтервал 08–12) плюс одне в понеділок о 20:00.
        var tuesdays = new[] { D(11, 10, 10), D(11, 3, 10), D(10, 27, 10), D(10, 20, 10), D(10, 13, 10) };
        var tasks = tuesdays.Select(d => new AnalyticsTask(null, null, d)).ToList();

        var few = Calc(AnalyticsPeriod.Month, tasks.Take(4).ToList());
        Assert.Null(few.ProductiveDay);
        Assert.Equal(4, few.InsightSample);

        tasks.Add(new AnalyticsTask(null, null, D(11, 9, 20)));
        var enough = Calc(AnalyticsPeriod.Month, tasks);
        Assert.Equal(DayOfWeek.Tuesday, enough.ProductiveDay);
        Assert.Equal(2, enough.ProductiveHourBlock);
        Assert.Equal(6, enough.InsightSample);
    }

    [Fact]
    public void HabitRegularity_UsesFirstLogAsStart_AndCapsAtExpected()
    {
        AnalyticsHabit[] allDone =
        [
            new(HabitFrequencyType.Daily, 0, null, [new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 10)])
        ];
        Assert.Equal(1.0, Calc(AnalyticsPeriod.Week, habits: allDone).HabitRegularity);

        AnalyticsHabit[] half =
        [
            new(HabitFrequencyType.Daily, 0, null, [new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 9)])
        ];
        Assert.Equal(0.5, Calc(AnalyticsPeriod.Week, habits: half).HabitRegularity);
    }

    [Fact]
    public void Streaks_TakeBestAcrossHabits()
    {
        AnalyticsHabit[] habits =
        [
            new(HabitFrequencyType.Daily, 0, null,
                [new DateOnly(2026, 11, 8), new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 10)]),
            new(HabitFrequencyType.Daily, 0, null, [new DateOnly(2026, 11, 10)])
        ];

        var result = Calc(AnalyticsPeriod.Week, habits: habits);

        Assert.Equal(3, result.CurrentStreak);
        Assert.Equal(3, result.BestStreak);
    }

    [Fact]
    public void Heatmap_CombinesTasksAndHabits_AndMarksFutureDays()
    {
        AnalyticsTask[] tasks = [new(null, null, D(11, 10, 9)), new(null, null, D(11, 10, 15))];
        AnalyticsHabit[] habits = [new(HabitFrequencyType.Daily, 0, null, [new DateOnly(2026, 11, 10)])];

        var result = Calc(AnalyticsPeriod.Week, tasks, habits);

        Assert.Equal(AnalyticsCalculator.HeatmapWeeks * 7, result.HeatmapCounts.Length);
        Assert.Equal(new DateOnly(2026, 7, 20), result.HeatmapStart);
        Assert.Equal(3, result.HeatmapCounts[113]); // сьогодні
        Assert.Equal(-1, result.HeatmapCounts[114]); // завтра
        Assert.Equal(-1, result.HeatmapCounts[^1]);
    }
}