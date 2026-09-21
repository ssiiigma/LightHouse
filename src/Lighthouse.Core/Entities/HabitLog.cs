namespace Lighthouse.Core.Entities;

/// <summary>Відмітка виконання звички за конкретний день (унікальна пара HabitId + Date).</summary>
public class HabitLog : BaseEntity
{
    public Guid HabitId { get; set; }
    public DateOnly Date { get; set; }
    public DateTime CompletedAtUtc { get; set; } = DateTime.UtcNow;
}
