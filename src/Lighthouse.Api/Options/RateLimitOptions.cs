namespace Lighthouse.Api.Options;

/// <summary>Ліміти запитів (фіксоване вікно). Значення читаються під час запиту, тому тести можуть їх підміняти.</summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Запитів до /api/auth/* на одну IP-адресу за вікно.</summary>
    public int AuthPermitLimit { get; set; } = 10;
    public int AuthWindowSeconds { get; set; } = 60;

    /// <summary>Запитів до решти API на користувача (за JWT) або IP-адресу за вікно.</summary>
    public int ApiPermitLimit { get; set; } = 300;
    public int ApiWindowSeconds { get; set; } = 60;
}