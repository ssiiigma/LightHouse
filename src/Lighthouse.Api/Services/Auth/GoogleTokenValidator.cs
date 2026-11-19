using Google.Apis.Auth;
using Lighthouse.Api.Options;
using Microsoft.Extensions.Options;

namespace Lighthouse.Api.Services.Auth;

public sealed record GoogleIdentity(string Subject, string Email, bool EmailVerified, string? Name);

/// <summary>Перевірка ID-токена Google (підпис, видавець, строк дії, аудиторія).</summary>
public interface IGoogleTokenValidator
{
    /// <returns>null, якщо токен недійсний.</returns>
    Task<GoogleIdentity?> ValidateAsync(string idToken, CancellationToken ct);
}

public sealed class GoogleTokenValidator(IOptions<GoogleOptions> options, ILogger<GoogleTokenValidator> logger)
    : IGoogleTokenValidator
{
    public async Task<GoogleIdentity?> ValidateAsync(string idToken, CancellationToken ct)
    {
        var audience = options.Value.ClientIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToArray();
        if (audience.Length == 0)
        {
            logger.LogError("Google:ClientIds is not configured; Google sign-in is unavailable.");
            return null;
        }

        try
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(
                idToken, new GoogleJsonWebSignature.ValidationSettings { Audience = audience });
            return new GoogleIdentity(payload.Subject, payload.Email, payload.EmailVerified, payload.Name);
        }
        catch (InvalidJwtException)
        {
            return null; // токен не пройшов перевірку (вміст у журнал не пишемо)
        }
    }
}