using Android.App;
using Android.Content;
using Lighthouse.Mobile.Localization;

namespace Lighthouse.Mobile;

/// <summary>
/// Канали сповіщень із високою важливістю: саме вони дозволяють показувати плашку поверх екрана (heads-up) зі звуком.
/// Важливість наявного каналу змінити не можна, тому при зміні налаштувань змінюється суфікс ідентифікатора.
/// </summary>
internal static class NotificationChannels
{
    public const string Reminders = "lighthouse_reminders_v2";
    public const string Push = "lighthouse_push_v2";

    public static void EnsureCreated(Context context)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26)
            || context.GetSystemService(Context.NotificationService) is not NotificationManager manager)
        {
            return;
        }

        var loc = LocalizationService.Instance;
        Create(manager, Reminders, loc["Channel_Reminders"], loc["Channel_RemindersDescription"]);
        Create(manager, Push, loc["Channel_Push"], loc["Channel_PushDescription"]);
    }

    private static void Create(NotificationManager manager, string id, string name, string description)
    {
        if (manager.GetNotificationChannel(id) is not null)
        {
            return; // не перезаписуємо вибір користувача в системних налаштуваннях
        }

        var channel = new NotificationChannel(id, name, NotificationImportance.High) { Description = description };
        channel.EnableVibration(true);
        manager.CreateNotificationChannel(channel);
    }
}