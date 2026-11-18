namespace Lighthouse.Shared.Contracts.Notifications;

/// <summary>
/// Налаштування сповіщень. Тихий режим увімкнений, коли задано обидва значення; щоденний огляд і розумні нагадування
/// надсилає сервер через FCM.
/// </summary>
public sealed record NotificationSettingsDto(
    bool TaskRemindersEnabled,
    bool HabitRemindersEnabled,
    bool DailyDigestEnabled,
    bool SmartRemindersEnabled,
    TimeOnly? QuietHoursStart,
    TimeOnly? QuietHoursEnd);

public sealed record RegisterDeviceRequest(string FcmToken);