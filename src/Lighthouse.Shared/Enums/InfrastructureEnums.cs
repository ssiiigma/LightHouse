namespace Lighthouse.Shared.Enums;

/// <summary>Призначення одноразового токена з листа: 0 — підтвердження e-mail, 1 — скидання пароля.</summary>
public enum EmailTokenPurpose
{
    Confirm = 0,
    Reset = 1
}

/// <summary>Тип резервної копії: 0 — автоматична (щодобова), 1 — створена вручну.</summary>
public enum BackupKind
{
    Auto = 0,
    Manual = 1
}

/// <summary>Тип серверного сповіщення (збігається з перемикачами NotificationSettings).</summary>
public enum NotificationKind
{
    TaskReminder = 0,
    HabitReminder = 1,
    DailyDigest = 2,
    SmartReminder = 3
}

public enum NotificationStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2
}