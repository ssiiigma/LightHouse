using Lighthouse.Shared.Enums;

namespace Lighthouse.Core.Entities;

/// <summary>Одноразовий токен із листа (підтвердження e-mail, скидання пароля); у БД лише SHA-256-хеш.</summary>
public class EmailToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public EmailTokenPurpose Purpose { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
}