using Lighthouse.Shared.Scheduling;
using Xunit;

namespace Lighthouse.Core.Tests;

public class ScheduleAdvisorTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 11, 0, 0);

    /// <summary>n виконаних завдань о 10:00 (блок 08–12) в останні дні.</summary>
    private static List<DateTime> MorningHabit(int n) =>
        Enumerable.Range(1, n).Select(i => Now.Date.AddDays(-i).AddHours(10)).ToList();

    private static ScheduleTask Reminder(DateTime reminder, DateTime? deadline = null) =>
        new(Guid.NewGuid(), reminder, deadline);

    [Fact]
    public void ReminderInLowBlock_IsMovedToMiddleOfBestBlock()
    {
        var task = Reminder(new DateTime(2026, 11, 11, 2, 0, 0), new DateTime(2026, 11, 15, 12, 0, 0));

        var result = Assert.Single(ScheduleAdvisor.Suggest(Now, MorningHabit(12), [task]));

        Assert.Equal(task.Id, result.TaskId);
        Assert.Equal(new DateTime(2026, 11, 11, 10, 0, 0), result.ProposedLocal);
        Assert.Equal(0, result.CurrentBlock);
        Assert.Equal(2, result.BestBlock);
    }

    [Fact]
    public void ReminderInBestBlock_GetsNoAdvice()
    {
        var task = Reminder(new DateTime(2026, 11, 11, 9, 0, 0));

        Assert.Empty(ScheduleAdvisor.Suggest(Now, MorningHabit(12), [task]));
    }

    [Fact]
    public void TooFewCompletedTasks_GivesNoAdvice()
    {
        var task = Reminder(new DateTime(2026, 11, 11, 2, 0, 0));

        Assert.Empty(ScheduleAdvisor.Suggest(Now, MorningHabit(ScheduleAdvisor.MinSample - 1), [task]));
    }

    [Fact]
    public void ProposedTimeAfterDeadline_GivesNoAdvice()
    {
        var task = Reminder(new DateTime(2026, 11, 11, 2, 0, 0), new DateTime(2026, 11, 11, 8, 0, 0));

        Assert.Empty(ScheduleAdvisor.Suggest(Now, MorningHabit(12), [task]));
    }

    [Fact]
    public void ProposedTimeAlreadyPassedToday_MovesToNextDay()
    {
        var task = Reminder(new DateTime(2026, 11, 10, 22, 0, 0));

        var result = Assert.Single(ScheduleAdvisor.Suggest(Now, MorningHabit(12), [task]));

        Assert.Equal(new DateTime(2026, 11, 11, 10, 0, 0), result.ProposedLocal);
    }

    [Fact]
    public void PastReminders_AreIgnored()
    {
        var task = Reminder(new DateTime(2026, 11, 10, 2, 0, 0));

        Assert.Empty(ScheduleAdvisor.Suggest(Now, MorningHabit(12), [task]));
    }
}