using Lighthouse.Shared.Contracts.Notifications;

namespace Lighthouse.Shared.Notifications;

/// <summary>Відображення локальних налаштувань пристрою на контракт сервера й назад.</summary>
public static class NotificationPrefsMapping
{
    public static NotificationSettingsDto ToDto(NotificationPrefs prefs, bool dailyDigest, bool smartReminders) => new(
        prefs.TaskReminders, prefs.HabitReminders, dailyDigest, smartReminders,
        prefs.QuietEnabled ? TimeOnly.FromTimeSpan(prefs.QuietStart) : null,
        prefs.QuietEnabled ? TimeOnly.FromTimeSpan(prefs.QuietEnd) : null);

    /// <summary>Застосовує серверні налаштування; коли тихий режим вимкнений, локальні час початку й кінця зберігаються.</summary>
    public static NotificationPrefs Apply(NotificationSettingsDto dto, NotificationPrefs current)
    {
        var withFlags = current with
        {
            TaskReminders = dto.TaskRemindersEnabled,
            HabitReminders = dto.HabitRemindersEnabled
        };

        return dto.QuietHoursStart is { } start && dto.QuietHoursEnd is { } end
            ? withFlags with { QuietEnabled = true, QuietStart = start.ToTimeSpan(), QuietEnd = end.ToTimeSpan() }
            : withFlags with { QuietEnabled = false };
    }
}