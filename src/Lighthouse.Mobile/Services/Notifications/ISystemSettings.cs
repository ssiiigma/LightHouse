namespace Lighthouse.Mobile.Services.Notifications;

/// <summary>Стан системних дозволів і швидкий перехід до системних налаштувань Android.</summary>
public interface ISystemSettings
{
    bool CanScheduleExactAlarms { get; }
    bool IsIgnoringBatteryOptimizations { get; }
    void OpenExactAlarmSettings();
    void OpenBatteryOptimizationSettings();
    void OpenAppNotificationSettings();
}