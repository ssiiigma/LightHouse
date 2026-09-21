namespace Lighthouse.Core.Entities;

public class User : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    /// <summary>bcrypt-хеш; null для облікових записів, створених лише через Google.</summary>
    public string? PasswordHash { get; set; }
    public string? GoogleSubject { get; set; }
    public bool IsEmailConfirmed { get; set; }

    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEndUtc { get; set; }
    public string? TimeZoneId { get; set; }

    public NotificationSettings? NotificationSettings { get; set; }
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
