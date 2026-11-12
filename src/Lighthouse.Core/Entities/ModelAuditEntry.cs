namespace Lighthouse.Core.Entities;

/// <summary>Журнал аудиту перерахунку моделі користувача: версія знімка, кількість подій, середня винагорода.</summary>
public class ModelAuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public int ModelVersion { get; set; }
    public int EventsCount { get; set; }
    public double MeanReward { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}