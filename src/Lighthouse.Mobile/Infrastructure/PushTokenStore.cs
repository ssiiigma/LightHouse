namespace Lighthouse.Mobile.Infrastructure;

/// <summary>Останній токен FCM цього пристрою й мітка «зареєстровано на сервері».</summary>
internal static class PushTokenStore
{
    private const string TokenKey = "push.token";
    private const string RegisteredKey = "push.registered";

    /// <summary>Токен змінився (Firebase видав новий).</summary>
    public static event Action? Changed;

    public static string? Token => Preferences.Default.Get(TokenKey, string.Empty) is { Length: > 0 } token ? token : null;

    public static string Registered
    {
        get => Preferences.Default.Get(RegisteredKey, string.Empty);
        set => Preferences.Default.Set(RegisteredKey, value);
    }

    public static void Save(string token, bool notify)
    {
        if (Token == token)
        {
            return;
        }

        Preferences.Default.Set(TokenKey, token);
        if (notify)
        {
            Changed?.Invoke();
        }
    }
}