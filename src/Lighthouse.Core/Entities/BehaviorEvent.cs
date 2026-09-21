using Lighthouse.Shared.Enums;

namespace Lighthouse.Core.Entities;

/// <summary>Append-only журнал поведінки — сировина для LinUCB та аналітики (UC-13).</summary>
public class BehaviorEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public BehaviorEventType Type { get; set; }

    // Зв'язки без зовнішніх ключів: історія має пережити видалення завдання/звички.
    public Guid? TaskId { get; set; }
    public Guid? HabitId { get; set; }

    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
    public int HourOfDay { get; set; }
    public int WeekDay { get; set; }
    public string? PayloadJson { get; set; }
}
