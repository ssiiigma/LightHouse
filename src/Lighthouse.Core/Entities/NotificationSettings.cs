namespace Lighthouse.Core.Entities;

public class NotificationSettings
{
    /// <summary>Ключ збігається з ідентифікатором користувача (зв'язок 1:1).</summary>
    public Guid UserId { get; set; }
    public bool TaskRemindersEnabled { get; set; } = true;
    public bool HabitRemindersEnabled { get; set; } = true;
    public bool DailyDigestEnabled { get; set; }
    public bool SmartRemindersEnabled { get; set; } = true;
    public TimeOnly? QuietHoursStart { get; set; }
    public TimeOnly? QuietHoursEnd { get; set; }
}
