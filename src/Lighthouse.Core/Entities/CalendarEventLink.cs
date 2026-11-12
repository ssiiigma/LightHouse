namespace Lighthouse.Core.Entities;

/// <summary>Зв'язок завдання з подією Google Calendar (односторонній експорт завдань із дедлайном).</summary>
public class CalendarEventLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid TaskId { get; set; }
    public string GoogleEventId { get; set; } = string.Empty;
    public DateTime LastSyncedAtUtc { get; set; } = DateTime.UtcNow;
}