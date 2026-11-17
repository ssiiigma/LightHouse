using Plugin.LocalNotification;
using Plugin.LocalNotification.Core.Models;

namespace Lighthouse.Mobile.Services.Notifications;

public sealed class LocalNotifier : ILocalNotif
{
    public async Task<bool> AreEnabledAsync() =>
        await MainThread.InvokeOnMainThreadAsync(() => LocalNotificationCenter.Current.AreNotificationsEnabled());

    public async Task<bool> RequestPermissionAsync() =>
        await MainThread.InvokeOnMainThreadAsync(() => LocalNotificationCenter.Current.RequestNotificationPermission(
            new NotificationPermission { Android = { RequestPermissionToScheduleExactAlarm = true } }));

    public async Task ScheduleAsync(int id, string title, string body, DateTime localTime)
    {
        var request = new NotificationRequest
        {
            NotificationId = id,
            Title = title,
            Description = body,
            Schedule = new NotificationRequestSchedule { NotifyTime = localTime }
        };

        await LocalNotificationCenter.Current.Show(request);
    }

    public Task CancelAsync(int id)
    {
        LocalNotificationCenter.Current.Cancel(id);
        return Task.CompletedTask;
    }

    public Task CancelAllAsync()
    {
        LocalNotificationCenter.Current.CancelAll();
        return Task.CompletedTask;
    }
}