using Android.App;
using Android.Content;
using Android.OS;
using Android.Provider;
using Lighthouse.Mobile.Services.Notifications;
using AndroidUri = Android.Net.Uri;

namespace Lighthouse.Mobile;

public sealed class AndroidSystemSettings : ISystemSettings
{
    private static Context Context => Android.App.Application.Context;

    public bool CanScheduleExactAlarms
    {
        get
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(31))
            {
                return true;
            }

            var alarms = (AlarmManager?)Context.GetSystemService(Android.Content.Context.AlarmService);
            return alarms?.CanScheduleExactAlarms() ?? false;
        }
    }

    public bool IsIgnoringBatteryOptimizations
    {
        get
        {
            var power = (PowerManager?)Context.GetSystemService(Android.Content.Context.PowerService);
            return power?.IsIgnoringBatteryOptimizations(Context.PackageName) ?? false;
        }
    }

    public void OpenExactAlarmSettings()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            Start(new Intent(Settings.ActionRequestScheduleExactAlarm, AndroidUri.Parse($"package:{Context.PackageName}")));
        }
    }

    public void OpenBatteryOptimizationSettings() =>
        Start(new Intent(Settings.ActionIgnoreBatteryOptimizationSettings));

    public void OpenAppNotificationSettings()
    {
        var intent = new Intent(Settings.ActionAppNotificationSettings);
        intent.PutExtra(Settings.ExtraAppPackage, Context.PackageName);
        Start(intent);
    }

    private static void Start(Intent intent)
    {
        intent.AddFlags(ActivityFlags.NewTask);
        Context.StartActivity(intent);
    }
}