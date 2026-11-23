namespace Lighthouse.Api.Options;

public sealed class FirebaseOptions
{
    public const string SectionName = "Firebase";

    /// <summary>Шлях до JSON-ключа service account (секрет; лише в user-secrets або змінних середовища). Порожній — push вимкнено.</summary>
    public string ServiceAccountPath { get; set; } = string.Empty;
}

public sealed class PushOptions
{
    public const string SectionName = "Push";

    /// <summary>Година щоденного огляду за місцевим часом користувача.</summary>
    public int DigestHour { get; set; } = 8;
}