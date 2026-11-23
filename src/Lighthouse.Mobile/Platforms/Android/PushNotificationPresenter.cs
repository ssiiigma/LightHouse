using Android.App;
using Android.Content;
using AndroidX.Core.App;
using Lighthouse.Mobile.Localization;

namespace Lighthouse.Mobile;

/// <summary>Показує серверне повідомлення: сервер надсилає лише тип і дані, текст формується локально.</summary>
internal static class PushNotificationPresenter
{

    public static void Show(Context context, IDictionary<string, string> data)
    {
        data.TryGetValue("kind", out var kind);
        var loc = LocalizationService.Instance;
        data.TryGetValue("count", out var count);
        data.TryGetValue("task", out var task);

        var (title, body, id) = kind switch
        {
            "digest" => (loc["Push_DigestTitle"], loc.Get("Push_DigestBody", count ?? "0", task ?? string.Empty), 9001),
            "smart" => (loc["Push_SmartTitle"], loc.Get("Push_SmartBody", task ?? string.Empty), 9002),
            "test" => (loc["Push_TestTitle"], loc["Push_TestBody"], 9003),
            _ => (string.Empty, string.Empty, 0)
        };

        if (title.Length == 0 || context.GetSystemService(Context.NotificationService) is not NotificationManager manager
            || !manager.AreNotificationsEnabled())
        {
            return;
        }

        NotificationChannels.EnsureCreated(context);

        var launch = context.PackageManager!.GetLaunchIntentForPackage(context.PackageName!);
        var pending = PendingIntent.GetActivity(
            context, id, launch, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        var notification = new NotificationCompat.Builder(context, NotificationChannels.Push)
            .SetSmallIcon(context.ApplicationInfo!.Icon)!
            .SetContentTitle(title)!
            .SetContentText(body)!
            .SetStyle(new NotificationCompat.BigTextStyle().BigText(body))!
            .SetPriority(NotificationCompat.PriorityHigh)!
            .SetCategory(NotificationCompat.CategoryReminder)!
            .SetContentIntent(pending)!
            .SetAutoCancel(true)!
            .Build()!;

        manager.Notify(id, notification);
    }
}