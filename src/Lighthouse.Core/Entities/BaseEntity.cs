namespace Lighthouse.Core.Entities;

/// <summary>
/// Базова сутність, що синхронізується між клієнтом і сервером.
/// Id генерується на клієнті; видалення «м'яке» (IsDeleted) — потрібне для синхронізації та архіву (UC-09).
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Момент останнього редагування (час клієнта, обмежений часом сервера) — для вирішення конфліктів.</summary>
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Момент останнього прийнятого запису на сервері — курсор для pull-синхронізації.</summary>
    public DateTime ServerModifiedAtUtc { get; set; } = DateTime.UtcNow;

    public bool IsDeleted { get; set; }
}
