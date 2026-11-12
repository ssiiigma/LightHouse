using Lighthouse.Shared.Enums;

namespace Lighthouse.Core.Entities;

/// <summary>Черга серверних сповіщень (FCM). TaskId і HabitId — логічні посилання без зовнішніх ключів.</summary>
public class NotificationQueueItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public NotificationKind Kind { get; set; }
    public Guid? TaskId { get; set; }
    public Guid? HabitId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTime ScheduledAtUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public int Attempts { get; set; }
    public NotificationStatus Status { get; set; } = NotificationStatus.Pending;
}