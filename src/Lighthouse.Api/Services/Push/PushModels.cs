using Lighthouse.Shared.Enums;

namespace Lighthouse.Api.Services.Push;

/// <summary>Вид серверного push; числа збігаються зі значеннями NotificationQueue.Kind (2 — огляд, 3 — розумне нагадування).</summary>
public enum PushKind
{
    Digest = 2,
    Smart = 3
}

public enum PushResult
{
    Sent,

    /// <summary>Токен недійсний (застосунок видалено або токен змінився): його треба забути.</summary>
    Unregistered,
    Failed,
    Disabled
}

public sealed record PushTask(Guid Id, string Title, TaskPriority Priority, DateTime? DeadlineUtc);

public sealed record PushUserSnapshot(
    DateTime NowUtc,
    TimeZoneInfo Zone,
    TimeSpan DigestAt,
    bool DigestEnabled,
    bool SmartEnabled,
    TimeOnly? QuietStart,
    TimeOnly? QuietEnd,
    IReadOnlyList<PushTask> OpenTasks,
    IReadOnlyList<DateTime> CompletedUtc,
    bool DigestSentToday,
    bool SmartSentToday);

public sealed record PlannedPush(PushKind Kind, int Count, PushTask Task);

public interface IPushSender
{
    bool IsConfigured { get; }
    Task<PushResult> SendAsync(string deviceToken, IReadOnlyDictionary<string, string> data, CancellationToken ct);
}

/// <summary>Журнал відправлених push (таблиця NotificationQueue): захищає від повторних відправлень протягом доби.</summary>
public interface IPushLog
{
    Task<bool> SentBetweenAsync(Guid userId, PushKind kind, DateTime fromUtc, DateTime toUtc, CancellationToken ct);
    Task RecordAsync(Guid userId, PushKind kind, Guid? taskId, DateTime sentAtUtc, CancellationToken ct);
}