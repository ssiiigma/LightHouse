using Lighthouse.Api.Services.Calendar;

namespace Lighthouse.Api.Tests;

public class CalendarEventMapperTests
{
    private static readonly DateTime Deadline = new(2026, 11, 20, 15, 0, 0, DateTimeKind.Utc);

    private static CalendarTaskInfo Task(
        int? estimate = null, DateTime? reminder = null, string title = "Звіт", string? description = null) =>
        new(Guid.NewGuid(), title, description, Deadline, reminder, estimate, false, false, Deadline);

    [Fact]
    public void Event_EndsAtDeadline_AndLastsDefaultOrEstimatedTime()
    {
        var byDefault = CalendarEventMapper.ToEvent(Task());
        Assert.Equal(Deadline, byDefault.EndUtc);
        Assert.Equal(Deadline.AddMinutes(-30), byDefault.StartUtc);

        var estimated = CalendarEventMapper.ToEvent(Task(estimate: 90));
        Assert.Equal(Deadline.AddMinutes(-90), estimated.StartUtc);
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(5000, 1440)]
    public void EstimatedDuration_IsClamped(int estimate, int expectedMinutes)
    {
        var data = CalendarEventMapper.ToEvent(Task(estimate: estimate));

        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), data.EndUtc - data.StartUtc);
    }

    [Fact]
    public void Reminder_BecomesMinutesBeforeStart_AndIsClamped()
    {
        var start = Deadline.AddMinutes(-30);

        Assert.Equal(60, CalendarEventMapper.ToEvent(Task(reminder: start.AddHours(-1))).ReminderMinutes);
        Assert.Equal(0, CalendarEventMapper.ToEvent(Task(reminder: Deadline.AddMinutes(-5))).ReminderMinutes);
        Assert.Equal(CalendarEventMapper.MaxReminderMinutes,
            CalendarEventMapper.ToEvent(Task(reminder: start.AddDays(-90))).ReminderMinutes);
        Assert.Null(CalendarEventMapper.ToEvent(Task()).ReminderMinutes);
    }

    [Fact]
    public void Text_IsTrimmedAndMarked()
    {
        var plain = CalendarEventMapper.ToEvent(Task());
        Assert.Equal("Lighthouse", plain.Description);

        var described = CalendarEventMapper.ToEvent(Task(title: "  Тема  ", description: " Деталі "));
        Assert.Equal("Тема", described.Summary);
        Assert.StartsWith("Деталі", described.Description);
        Assert.EndsWith("— Lighthouse", described.Description);

        Assert.Equal(1024, CalendarEventMapper.ToEvent(Task(title: new string('x', 3000))).Summary.Length);
    }

    [Fact]
    public void TaskWithoutDeadline_CannotBeMapped()
    {
        var task = Task() with { DeadlineUtc = null };

        Assert.Throws<ArgumentException>(() => CalendarEventMapper.ToEvent(task));
    }
}