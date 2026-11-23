using Lighthouse.Shared.Contracts.Notifications;
using Lighthouse.Shared.Notifications;

namespace Lighthouse.Mobile.Services.Notifications;

/// <summary>Налаштування нагадувань на пристрої з прапорцем «потрібно надіслати на сервер».</summary>
public interface INotifSettings
{
    /// <summary>Будь-яка зміна (планувальник нагадувань перераховується).</summary>
    event Action? Changed;

    /// <summary>Зміна користувачем (її треба надіслати на сервер).</summary>
    event Action? UserChanged;

    NotificationPrefs Load();

    /// <summary>Зберігає зміну користувача й позначає її як ненадіслану.</summary>
    void Save(NotificationPrefs prefs);

    /// <summary>Застосовує налаштування сервера; ігнорується, поки є ненадіслані зміни користувача.</summary>
    void ApplyFromServer(NotificationSettingsDto settings);

    NotificationSettingsDto ToDto();

    bool IsDirty { get; }

    /// <summary>Номер версії локальних налаштувань: зростає при кожній зміні користувачем.</summary>
    long Revision { get; }

    /// <summary>Знімає прапорець, лише якщо після надсилання користувач нічого не змінив.</summary>
    void MarkClean(long revision);

    void ResetSyncState();
    
    (bool DailyDigest, bool SmartReminders) GetServerFlags();

    /// <summary>Щоденний огляд і розумні нагадування (їх надсилає сервер): зміна користувача, яку треба передати на сервер.</summary>
    void SaveServerFlags(bool dailyDigest, bool smartReminders);
}

public sealed class NotificationSettingsStore : INotifSettings
{
    private const string Prefix = "notif.";

    public event Action? Changed;

    public event Action? UserChanged;

    public bool IsDirty => Preferences.Default.Get(Prefix + "dirty", false);

    public long Revision => Preferences.Default.Get(Prefix + "rev", 0L);

    private static bool DailyDigest => Preferences.Default.Get(Prefix + "digest", false);

    private static bool SmartReminders => Preferences.Default.Get(Prefix + "smart", true);

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
        Write(prefs);
        Preferences.Default.Set(Prefix + "rev", Revision + 1);
        Preferences.Default.Set(Prefix + "dirty", true);
        Changed?.Invoke();
        UserChanged?.Invoke();
    }

    public void ApplyFromServer(NotificationSettingsDto settings)
    {
        if (IsDirty)
        {
            return;
        }

        var current = Load();
        var applied = NotificationPrefsMapping.Apply(settings, current);
        if (applied == current && settings.DailyDigestEnabled == DailyDigest && settings.SmartRemindersEnabled == SmartReminders)
        {
            return;
        }

        Write(applied);
        Preferences.Default.Set(Prefix + "digest", settings.DailyDigestEnabled);
        Preferences.Default.Set(Prefix + "smart", settings.SmartRemindersEnabled);
        Changed?.Invoke();
    }

    public NotificationSettingsDto ToDto() => NotificationPrefsMapping.ToDto(Load(), DailyDigest, SmartReminders);

    public void MarkClean(long revision)
    {
        if (revision == Revision)
        {
            Preferences.Default.Set(Prefix + "dirty", false);
        }
    }

    public void ResetSyncState() => Preferences.Default.Set(Prefix + "dirty", false);

    private static void Write(NotificationPrefs prefs)
    {
        var p = Preferences.Default;
        p.Set(Prefix + "tasks", prefs.TaskReminders);
        p.Set(Prefix + "habits", prefs.HabitReminders);
        p.Set(Prefix + "quiet", prefs.QuietEnabled);
        p.Set(Prefix + "quiet.start", (int)prefs.QuietStart.TotalMinutes);
        p.Set(Prefix + "quiet.end", (int)prefs.QuietEnd.TotalMinutes);
    }
    
    public (bool DailyDigest, bool SmartReminders) GetServerFlags() => (DailyDigest, SmartReminders);

    public void SaveServerFlags(bool dailyDigest, bool smartReminders)
    {
        Preferences.Default.Set(Prefix + "digest", dailyDigest);
        Preferences.Default.Set(Prefix + "smart", smartReminders);
        Preferences.Default.Set(Prefix + "rev", Revision + 1);
        Preferences.Default.Set(Prefix + "dirty", true);
        Changed?.Invoke();
        UserChanged?.Invoke();
    }
}