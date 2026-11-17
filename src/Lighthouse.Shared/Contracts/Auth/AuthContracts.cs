namespace Lighthouse.Shared.Contracts.Auth;

/// <param name="Language">Мова листа підтвердження (uk | en); необов'язково.</param>
public sealed record RegisterRequest(string Name, string Email, string Password, string? Language = null);

public sealed record LoginRequest(string Email, string Password);

public sealed record GoogleLoginRequest(string IdToken);

public sealed record RefreshRequest(string RefreshToken);

public sealed record LogoutRequest(string RefreshToken);

/// <summary>Запит листа для скидання пароля; відповідь завжди однакова (202), чи існує обліковий запис, не розкривається.</summary>
public sealed record ForgotPasswordRequest(string Email, string? Language = null);

public sealed record ResetPasswordRequest(string Token, string NewPassword);

public sealed record ConfirmEmailRequest(string Token);

public sealed record UserDto(Guid Id, string Name, string Email);

public sealed record AuthResponse(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAtUtc,
    UserDto User);