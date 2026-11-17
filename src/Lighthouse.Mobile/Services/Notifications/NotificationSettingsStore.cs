using Lighthouse.Shared.Notifications;

namespace Lighthouse.Mobile.Services.Notifications;

/// <summary>Налаштування нагадувань на пристрої.</summary>
public interface INotifSettings
{
    event Action? Changed;
    NotificationPrefs Load();
    void Save(NotificationPrefs prefs);
}

public sealed class NotificationSettingsStore : INotifSettings
{
    private const string Prefix = "notif.";

    public event Action? Changed;

    public NotificationPrefs Load()
    {
        var d = NotificationPrefs.Default;
        var p = Preferences.Default;
        return new NotificationPrefs(
            p.Get(Prefix + "tasks", d.TaskReminders),
            p.Get(Prefix + "habits", d.HabitReminders),
            p.Get(Prefix + "quiet", d.QuietEnabled),
            TimeSpan.FromMinutes(p.Get(Prefix + "quiet.start", (int)d.QuietStart.TotalMinutes)),
            TimeSpan.FromMinutes(p.Get(Prefix + "quiet.end", (int)d.QuietEnd.TotalMinutes)));
    }

    public void Save(NotificationPrefs prefs)
    {
        var p = Preferences.Default;
        p.Set(Prefix + "tasks", prefs.TaskReminders);
        p.Set(Prefix + "habits", prefs.HabitReminders);
        p.Set(Prefix + "quiet", prefs.QuietEnabled);
        p.Set(Prefix + "quiet.start", (int)prefs.QuietStart.TotalMinutes);
        p.Set(Prefix + "quiet.end", (int)prefs.QuietEnd.TotalMinutes);
        Changed?.Invoke();
    }
}