namespace Lighthouse.Api.Options;

public sealed class GoogleOptions
{
    public const string SectionName = "Google";

    /// <summary>Дозволені Client ID для перевірки ID-токена (значення claim «aud»).</summary>
    public string[] ClientIds { get; set; } = [];

    /// <summary>Web-клієнт проєкту: сервер підключає Google Calendar через код авторизації.</summary>
    public string WebClientId { get; set; } = string.Empty;

    /// <summary>Секрет Web-клієнта: лише в user-secrets або змінних середовища.</summary>
    public string WebClientSecret { get; set; } = string.Empty;
}