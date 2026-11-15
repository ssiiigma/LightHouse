namespace Lighthouse.Shared.Contracts.Profile;

/// <summary>Часовий пояс користувача (ідентифікатор IANA, наприклад Europe/Kyiv).</summary>
public sealed record TimeZoneDto(string TimeZoneId);