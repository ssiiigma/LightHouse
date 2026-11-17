using Lighthouse.Shared.Enums;
using Lighthouse.Shared.Notifications;
using Lighthouse.Shared.Scheduling;
using Xunit;

namespace Lighthouse.Core.Tests;

public class NotificationPlannerTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 12, 0, 0); // вівторок
    private static readonly NotificationPrefs On = NotificationPrefs.Default;

    private static PlanHabit Habit(
        HabitFrequencyType frequency = HabitFrequencyType.Daily, int mask = 0, int? times = null,
        int? minutes = 18 * 60, params DateOnly[] logs) =>
        new(Guid.NewGuid(), frequency, mask, times, minutes, logs);

    [Fact]
    public void TaskReminder_InFuture_IsPlanned_PastAndCompletedAreSkipped()
    {
        var future = new PlanTask(Guid.NewGuid(), Now.AddHours(3), false);
        var past = new PlanTask(Guid.NewGuid(), Now.AddHours(-1), false);
        var done = new PlanTask(Guid.NewGuid(), Now.AddHours(5), true);
        var none = new PlanTask(Guid.NewGuid(), null, false);

        var plan = NotificationPlanner.Plan(Now, On, [future, past, done, none], []);

        var item = Assert.Single(plan);
        Assert.Equal(future.Id, item.TargetId);
        Assert.Equal(NotificationKind.TaskReminder, item.Kind);
        Assert.Equal(Now.AddHours(3), item.AtLocal);
    }

    [Fact]
    public void DisabledCategories_ProduceNothing()
    {
        var off = On with { TaskReminders = false, HabitReminders = false };

        var plan = NotificationPlanner.Plan(Now, off,
            [new PlanTask(Guid.NewGuid(), Now.AddHours(3), false)], [Habit()]);

        Assert.Empty(plan);
    }

    [Fact]
    public void DailyHabit_PlansEveryDayInWindow_SkippingPassedTimeAndDoneDays()
    {
        Assert.Equal(14, NotificationPlanner.Plan(Now, On, [], [Habit()]).Count);

        var doneToday = Habit(logs: [new DateOnly(2026, 11, 10)]);
        Assert.Equal(13, NotificationPlanner.Plan(Now, On, [], [doneToday]).Count);

        var morning = Habit(minutes: 9 * 60); // сьогоднішній час 09:00 уже минув
        Assert.Equal(13, NotificationPlanner.Plan(Now, On, [], [morning]).Count);
    }

    [Fact]
    public void SpecificDaysHabit_PlansOnlySelectedWeekdays()
    {
        var mondayWednesday = Habit(HabitFrequencyType.SpecificDays, mask: 1 | 4);

        var plan = NotificationPlanner.Plan(Now, On, [], [mondayWednesday]);

        Assert.Equal(4, plan.Count); // ср 11, пн 16, ср 18, пн 23
        Assert.All(plan, p => Assert.Contains(p.AtLocal.DayOfWeek, new[] { DayOfWeek.Monday, DayOfWeek.Wednesday }));
    }

    [Fact]
    public void TimesPerWeekHabit_StopsReminding_WhenWeeklyTargetReached()
    {
        var reached = Habit(HabitFrequencyType.TimesPerWeek, times: 2,
            logs: [new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 10)]);

        var plan = NotificationPlanner.Plan(Now, On, [], [reached]);

        Assert.Equal(8, plan.Count); // тиждень до неділі 15-го пропускається, далі 16–23
        Assert.All(plan, p => Assert.True(p.AtLocal.Date >= new DateTime(2026, 11, 16)));
    }

    [Fact]
    public void Plan_IsCappedAndSortedByTime()
    {
        var habits = Enumerable.Range(0, 300).Select(_ => Habit()).ToList();

        var plan = NotificationPlanner.Plan(Now, On, [], habits);

        Assert.Equal(NotificationPlanner.MaxScheduled, plan.Count);
        Assert.True(plan.Zip(plan.Skip(1), (a, b) => a.AtLocal <= b.AtLocal).All(ok => ok));
    }

    [Fact]
    public void Ids_AreStable_AndUniquePerDay()
    {
        var habit = Habit();

        var first = NotificationPlanner.Plan(Now, On, [], [habit]);
        var second = NotificationPlanner.Plan(Now, On, [], [habit]);

        Assert.Equal(first.Select(p => p.Id), second.Select(p => p.Id));
        Assert.Equal(first.Count, first.Select(p => p.Id).Distinct().Count());
        Assert.All(first, p => Assert.True(p.Id > 0));
    }

    [Fact]
    public void QuietHours_ShiftPlannedReminderToEndOfWindow()
    {
        var quiet = On with { QuietEnabled = true };
        var task = new PlanTask(Guid.NewGuid(), new DateTime(2026, 11, 10, 23, 0, 0), false);

        var plan = NotificationPlanner.Plan(Now, quiet, [task], []);

        Assert.Equal(new DateTime(2026, 11, 11, 7, 0, 0), Assert.Single(plan).AtLocal);
    }

    [Theory]
    [InlineData(23, 30, 11, 7, 0)]  // після початку: наступного дня о 07:00
    [InlineData(3, 0, 10, 7, 0)]    // до кінця: того ж дня о 07:00
    [InlineData(7, 0, 10, 7, 0)]    // межа кінця не зсувається
    [InlineData(22, 0, 11, 7, 0)]   // межа початку зсувається
    [InlineData(12, 0, 10, 12, 0)]  // поза вікном без змін
    public void OvernightWindow_Shift(int hour, int minute, int expectedDay, int expectedHour, int expectedMinute)
    {
        var quiet = On with { QuietEnabled = true };

        var shifted = QuietHours.Shift(new DateTime(2026, 11, 10, hour, minute, 0), quiet);

        Assert.Equal(new DateTime(2026, 11, expectedDay, expectedHour, expectedMinute, 0), shifted);
    }

    [Fact]
    public void DaytimeWindow_AndDisabledOrEmptyWindow()
    {
        var daytime = new NotificationPrefs(true, true, true, new TimeSpan(13, 0, 0), new TimeSpan(15, 0, 0));
        Assert.Equal(new DateTime(2026, 11, 10, 15, 0, 0), QuietHours.Shift(new DateTime(2026, 11, 10, 14, 0, 0), daytime));

        var at = new DateTime(2026, 11, 10, 23, 0, 0);
        Assert.Equal(at, QuietHours.Shift(at, On));
        Assert.Equal(at, QuietHours.Shift(at, On with { QuietEnabled = true, QuietStart = TimeSpan.FromHours(8), QuietEnd = TimeSpan.FromHours(8) }));
    }

    [Fact]
    public void SuggestTime_UsesMiddleOfBestBlock_AndNeedsEnoughData()
    {
        var now = new DateTime(2026, 11, 10, 11, 0, 0);
        var morning = Enumerable.Range(1, 12).Select(i => now.Date.AddDays(-i).AddHours(10)).ToList();

        Assert.Equal(TimeSpan.FromHours(10), ScheduleAdvisor.SuggestTime(now, morning));
        Assert.Null(ScheduleAdvisor.SuggestTime(now, morning.Take(ScheduleAdvisor.MinSample - 1)));

        var old = Enumerable.Range(100, 20).Select(i => now.Date.AddDays(-i).AddHours(10));
        Assert.Null(ScheduleAdvisor.SuggestTime(now, old)); // старіше 90 діб не враховується
    }
}