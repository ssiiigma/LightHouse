using Lighthouse.Core.Habits;
using Lighthouse.Shared.Enums;
using Xunit;

namespace Lighthouse.Core.Tests;

public class HabitStreakCalculatorTests
{
    private static readonly DateOnly Tuesday = new(2026, 11, 10);
    private static readonly DateOnly Friday = new(2026, 11, 13);

    private static DateOnly D(int month, int day) => new(2026, month, day);

    private static HabitStreaks Daily(DateOnly today, params DateOnly[] logs) =>
        HabitStreakCalculator.Calculate(HabitFrequencyType.Daily, 0, null, logs, today);

    [Fact]
    public void NoLogs_ReturnsZeroStreaks()
    {
        Assert.Equal(new HabitStreaks(0, 0), Daily(Tuesday));
    }

    [Fact]
    public void Daily_ConsecutiveDaysEndingToday_CountsAll()
    {
        Assert.Equal(new HabitStreaks(3, 3), Daily(Tuesday, D(11, 8), D(11, 9), D(11, 10)));
    }

    [Fact]
    public void Daily_TodayNotYetLogged_DoesNotBreakStreak()
    {
        Assert.Equal(new HabitStreaks(2, 2), Daily(Tuesday, D(11, 8), D(11, 9)));
    }

    [Fact]
    public void Daily_MissedYesterday_ResetsCurrentButKeepsBest()
    {
        Assert.Equal(new HabitStreaks(0, 3), Daily(Tuesday, D(11, 6), D(11, 7), D(11, 8)));
    }

    [Fact]
    public void Daily_GapInMiddle_BestIsLongestRun()
    {
        var result = Daily(Tuesday, D(11, 1), D(11, 2), D(11, 3), D(11, 9), D(11, 10));

        Assert.Equal(new HabitStreaks(2, 3), result);
    }

    [Fact]
    public void Daily_IgnoresFutureLogsAndDuplicates()
    {
        var result = Daily(Tuesday, D(11, 10), D(11, 10), D(11, 12));

        Assert.Equal(new HabitStreaks(1, 1), result);
    }

    [Fact]
    public void SpecificDays_UnscheduledDaysDoNotBreakStreak()
    {
        const int monWedFri = 1 | 4 | 16;

        var result = HabitStreakCalculator.Calculate(
            HabitFrequencyType.SpecificDays, monWedFri, null, [D(11, 9), D(11, 11), D(11, 13)], Friday);

        Assert.Equal(new HabitStreaks(3, 3), result);
    }

    [Fact]
    public void SpecificDays_MissedScheduledDay_BreaksStreak()
    {
        const int monWedFri = 1 | 4 | 16;

        var result = HabitStreakCalculator.Calculate(
            HabitFrequencyType.SpecificDays, monWedFri, null, [D(11, 9), D(11, 13)], Friday);

        Assert.Equal(new HabitStreaks(1, 1), result);
    }

    [Fact]
    public void TimesPerWeek_IncompleteCurrentWeek_DoesNotBreakStreak()
    {
        var result = HabitStreakCalculator.Calculate(
            HabitFrequencyType.TimesPerWeek, 0, 2, [D(11, 3), D(11, 5), D(11, 10)], Friday);

        Assert.Equal(new HabitStreaks(1, 1), result);
    }

    [Fact]
    public void TimesPerWeek_CompletedCurrentWeek_ExtendsStreak()
    {
        var result = HabitStreakCalculator.Calculate(
            HabitFrequencyType.TimesPerWeek, 0, 2, [D(11, 3), D(11, 5), D(11, 10), D(11, 12)], Friday);

        Assert.Equal(new HabitStreaks(2, 2), result);
    }

    [Fact]
    public void TimesPerWeek_MissedWeek_BreaksStreak()
    {
        var result = HabitStreakCalculator.Calculate(
            HabitFrequencyType.TimesPerWeek, 0, 2,
            [D(10, 27), D(10, 29), D(11, 3), D(11, 10), D(11, 12)], Friday);

        Assert.Equal(new HabitStreaks(1, 1), result);
    }
}