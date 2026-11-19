namespace Lighthouse.Api.Options;

public sealed class GoogleOptions
{
    public const string SectionName = "Google";

    /// <summary>Дозволені Client ID (значення claim «aud»): Android-клієнт застосунку й Web-клієнт проєкту.</summary>
    public string[] ClientIds { get; set; } = [];
}