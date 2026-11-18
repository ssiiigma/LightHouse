namespace Lighthouse.Shared.Contracts.Profile;

/// <summary>Часовий пояс користувача (ідентифікатор IANA, наприклад Europe/Kyiv).</summary>
public sealed record TimeZoneDto(string TimeZoneId);

public sealed record ProfileDto(Guid Id, string Name, string Email, string? TimeZoneId, bool IsEmailConfirmed);

public sealed record UpdateProfileRequest(string Name);