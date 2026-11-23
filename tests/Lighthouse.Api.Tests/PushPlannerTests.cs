using Lighthouse.Api.Services.Push;
using Lighthouse.Shared.Enums;

namespace Lighthouse.Api.Tests;

public class PushPlannerTests
{
    private static readonly TimeZoneInfo Plus2 = TimeZoneInfo.CreateCustomTimeZone("UTC+2", TimeSpan.FromHours(2), "UTC+2", "UTC+2");

    // 10 листопада 2026 р., місцевий час = UTC + 2
    private static DateTime Local(int hour, int minute) =>
        new DateTime(2026, 11, 10, hour, minute, 0, DateTimeKind.Utc).AddHours(-2);

    private static PushTask Task(string title, TaskPriority priority, DateTime? deadlineUtc) =>
        new(Guid.NewGuid(), title, priority, deadlineUtc);

    private static PushUserSnapshot Snapshot(
        DateTime nowUtc, IReadOnlyList<PushTask>? tasks = null, bool digest = true, bool smart = true,
        bool digestSent = false, bool smartSent = false, TimeOnly? quietStart = null, TimeOnly? quietEnd = null,
        IReadOnlyList<DateTime>? completed = null) =>
        new(nowUtc, Plus2, TimeSpan.FromHours(8), digest, smart, quietStart, quietEnd,
            tasks ?? [], completed ?? [], digestSent, smartSent);

    private static List<DateTime> MorningCompletions(int count) =>
        Enumerable.Range(1, count).Select(i => Local(10, 0).AddDays(-i)).ToList(); // 10:00 місцевого → блок 08–12

    private static readonly PushTask Overdue = Task("Звіт", TaskPriority.High, new DateTime(2026, 11, 9, 10, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void Digest_IsPlannedInsideTheWindow_WithCountAndTopTask()
    {
        var tasks = new[]
        {
            Task("Дрібниця", TaskPriority.Low, Local(15, 0)),
            Task("Важливе", TaskPriority.Critical, Local(12, 0)),
            Task("Без дедлайну", TaskPriority.High, null)
        };

        var plan = PushPlanner.Plan(Snapshot(Local(8, 30), tasks));

        var push = Assert.Single(plan);
        Assert.Equal(PushKind.Digest, push.Kind);
        Assert.Equal(2, push.Count); // лише завдання з дедлайном на сьогодні чи раніше
        Assert.Equal("Важливе", push.Task.Title);
    }

    [Theory]
    [InlineData(7, 59)]
    [InlineData(10, 0)]
    [InlineData(14, 30)]
    public void Digest_IsNotPlannedOutsideTheWindow(int hour, int minute) =>
        Assert.Empty(PushPlanner.Plan(Snapshot(Local(hour, minute), [Overdue], smart: false)));

    [Fact]
    public void Digest_IsSkipped_WhenAlreadySentDisabledOrNothingIsDue()
    {
        Assert.Empty(PushPlanner.Plan(Snapshot(Local(8, 30), [Overdue], smart: false, digestSent: true)));
        Assert.Empty(PushPlanner.Plan(Snapshot(Local(8, 30), [Overdue], digest: false, smart: false)));
        Assert.Empty(PushPlanner.Plan(Snapshot(Local(8, 30), [Task("Пізніше", TaskPriority.High, Local(8, 30).AddDays(3))], smart: false)));
    }

    [Fact]
    public void QuietHours_SuppressEverything()
    {
        var plan = PushPlanner.Plan(Snapshot(
            Local(8, 30), [Overdue], quietStart: new TimeOnly(22, 0), quietEnd: new TimeOnly(9, 0)));

        Assert.Empty(plan);
    }

    [Fact]
    public void Smart_IsPlannedAtTheProductiveTime_ForImportantTasks()
    {
        var tasks = new[]
        {
            Task("Звичайне", TaskPriority.Medium, null),
            Task("Важливе", TaskPriority.High, null)
        };

        var plan = PushPlanner.Plan(Snapshot(Local(10, 20), tasks, digest: false, completed: MorningCompletions(12)));

        var push = Assert.Single(plan);
        Assert.Equal(PushKind.Smart, push.Kind);
        Assert.Equal("Важливе", push.Task.Title);
    }

    [Fact]
    public void Smart_NeedsHistoryImportantTasksAndTheRightMoment()
    {
        var important = new[] { Task("Важливе", TaskPriority.High, null) };
        var history = MorningCompletions(12);

        Assert.Empty(PushPlanner.Plan(Snapshot(Local(10, 20), important, completed: MorningCompletions(3)))); // замало даних
        Assert.Empty(PushPlanner.Plan(Snapshot(Local(10, 20), [Task("Звичайне", TaskPriority.Medium, null)], completed: history)));
        Assert.Empty(PushPlanner.Plan(Snapshot(Local(11, 30), important, completed: history)));             // поза вікном
        Assert.Empty(PushPlanner.Plan(Snapshot(Local(10, 20), important, completed: history, smartSent: true)));
        Assert.Empty(PushPlanner.Plan(Snapshot(Local(10, 20), important, smart: false, completed: history)));
    }

    [Fact]
    public void Digest_TakesPrecedenceOverSmartInTheSameRun()
    {
        var snapshot = Snapshot(Local(8, 30), [Overdue], completed: Enumerable.Range(1, 12).Select(i => Local(8, 30).AddDays(-i)).ToList());

        Assert.Equal(PushKind.Digest, Assert.Single(PushPlanner.Plan(snapshot)).Kind);
    }
}