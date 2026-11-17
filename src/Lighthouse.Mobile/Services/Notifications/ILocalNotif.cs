namespace Lighthouse.Mobile.Services.Notifications;

/// <summary>Інфраструктура: планування локальних сповіщень (обгортка над Plugin.LocalNotification).</summary>
public interface ILocalNotif
{
    Task<bool> AreEnabledAsync();

    /// <summary>Запитує дозвіл на сповіщення (Android 13+) і на точні будильники.</summary>
    Task<bool> RequestPermissionAsync();

    Task ScheduleAsync(int id, string title, string body, DateTime localTime);
    Task CancelAsync(int id);
    Task CancelAllAsync();
}