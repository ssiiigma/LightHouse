using Lighthouse.Api.Infrastructure;

namespace Lighthouse.Api.Services.Calendar;

/// <summary>
/// Подія календаря для завдання: закінчується в момент дедлайну, починається за «орієнтовну тривалість» до нього
/// (за замовчуванням 30 хв). Нагадування завдання стає сповіщенням події (до 4 тижнів).
/// </summary>
public static class CalendarEventMapper
{
    public const int DefaultMinutes = 30;
    public const int MaxReminderMinutes = 40_320;

    public static CalendarEventData ToEvent(CalendarTaskInfo task)
    {
        var end = SyncTime.ToUtc(task.DeadlineUtc ?? throw new ArgumentException("The task has no deadline.", nameof(task)));
        var minutes = task.EstimatedMinutes is { } estimate ? Math.Clamp(estimate, 5, 1440) : DefaultMinutes;
        var start = end.AddMinutes(-minutes);

        int? reminder = task.ReminderAtUtc is { } at
            ? (int)Math.Clamp((start - SyncTime.ToUtc(at)).TotalMinutes, 0, MaxReminderMinutes)
            : null;

        var description = string.IsNullOrWhiteSpace(task.Description)
            ? "Lighthouse"
            : task.Description.Trim() + "\n\n— Lighthouse";

        return new CalendarEventData(task.Id, Cut(task.Title.Trim(), 1024), 
            Cut(description, 8000), start, end, reminder);
    }

    private static string Cut(string value, int max) => value.Length <= max ? value : value[..max];
}