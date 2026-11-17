using Lighthouse.Shared.Analytics;
using Lighthouse.Shared.Enums;
using Xunit;

namespace Lighthouse.Core.Tests;

public class RangeSummaryTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 12, 0, 0);

    private static DateTime D(int day, int hour = 12) => new(2026, 11, day, hour, 0, 0);

    [Fact]
    public void Summary_CountsCompletedAndPlanned_AndSplitsByDay()
    {
        var study = Guid.NewGuid();
        AnalyticsTask[] tasks =
        [
            new(study, null, D(9)),
            new(study, null, D(10)),
            new(null, null, D(10, 18)),
            new(null, D(11), null),      // відкрите з дедлайном у діапазоні
            new(null, null, D(2)),       // поза діапазоном
            new(null, null, null)        // відкрите без дедлайну
        ];

        var summary = AnalyticsCalculator.Summarize(
            new DateRange(new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 12)), Now, tasks, []);

        Assert.Equal(3, summary.Completed);
        Assert.Equal(4, summary.Planned);
        Assert.Equal(0.75, summary.CompletionRate);
        Assert.Equal(new[] { 1, 2, 0, 0 }, summary.Days.Select(d => d.Completed).ToArray());
        Assert.Equal(summary.Completed, summary.Days.Sum(d => d.Completed));
        Assert.Equal(study, summary.Categories[0].CategoryId);
        Assert.Equal(2, summary.Categories[0].Count);
    }

    [Fact]
    public void Summary_OfEmptyData_HasNoRates()
    {
        var summary = AnalyticsCalculator.Summarize(
            new DateRange(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30)), Now, [], []);

        Assert.Null(summary.CompletionRate);
        Assert.Null(summary.HabitRegularity);
        Assert.Equal(30, summary.Days.Count);
        Assert.Equal(0, summary.BestStreak);
    }

    [Fact]
    public void Summary_IncludesHabitRegularityAndStreaks()
    {
        AnalyticsHabit[] habits =
        [
            new(HabitFrequencyType.Daily, 0, null, [new DateOnly(2026, 11, 8), new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 10)])
        ];

        var summary = AnalyticsCalculator.Summarize(
            new DateRange(new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 15)), Now, [], habits);

        Assert.Equal(1.0, summary.HabitRegularity);
        Assert.Equal(3, summary.CurrentStreak);
    }
}