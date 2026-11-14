using Lighthouse.Shared.Enums;
using SQLite;

namespace Lighthouse.Mobile.Data;

/// <summary>Стан синхронізації локального запису.</summary>
public enum SyncState
{
    /// <summary>Збігається з сервером.</summary>
    Synced = 0,

    /// <summary>Є ще не надіслані зміни (запис у черзі Outbox).</summary>
    Pending = 1,

    /// <summary>Сервер відхилив зміну (помилка валідації тощо); дані лишаються на пристрої.</summary>
    Failed = 2
}

[Table("Categories")]
public sealed class LocalCategory
{
    [PrimaryKey] public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ColorHex { get; set; }
    public string? Icon { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime ServerModifiedAtUtc { get; set; }
    public bool IsDeleted { get; set; }
    public SyncState SyncState { get; set; }
}

[Table("Tasks")]
public sealed class LocalTask
{
    [PrimaryKey] public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime? DeadlineUtc { get; set; }
    public TaskPriority Priority { get; set; } = TaskPriority.Medium;
    public Guid? CategoryId { get; set; }
    public DateTime? ReminderAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int? EstimatedMinutes { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime ServerModifiedAtUtc { get; set; }

    /// <summary>М'яке видалення: запис не показується; після підтвердження сервером рядок видаляється.</summary>
    public bool IsDeleted { get; set; }
    public SyncState SyncState { get; set; }
}

[Table("Subtasks")]
public sealed class LocalSubtask
{
    [PrimaryKey] public Guid Id { get; set; }
    [Indexed] public Guid TaskId { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool IsDone { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>Черга вихідних змін: для кожного об'єкта зберігається лише остання операція.</summary>
[Table("Outbox")]
public sealed class OutboxItem
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [Indexed] public string EntityType { get; set; } = string.Empty;
    [Indexed] public Guid EntityId { get; set; }

    /// <summary>"put" або "delete".</summary>
    public string Operation { get; set; } = string.Empty;
    public string? PayloadJson { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}

/// <summary>Кеш останнього ранжування сервера (для показу без мережі); використовується в P4.</summary>
[Table("RankingCache")]
public sealed class RankingCache
{
    [PrimaryKey] public Guid TaskId { get; set; }
    public int Rank { get; set; }
    public double Score { get; set; }
    public string? ExplanationText { get; set; }
    public string? RecommendationKey { get; set; }
    public DateTime ComputedAtUtc { get; set; }
}