namespace Lighthouse.Api.Options;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "Lighthouse";
    public string Audience { get; set; } = "Lighthouse.Mobile";

    /// <summary>Симетричний ключ HS256, щонайменше 32 символи. У production — лише зі змінної середовища Jwt__Secret.</summary>
    public string Secret { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;
}
