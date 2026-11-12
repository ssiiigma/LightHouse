using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Lighthouse.Api.Services.Calendar;

/// <summary>Шифрування refresh-токена Google Calendar перед записом у БД (CalendarConnections.RefreshTokenProtected).</summary>
public interface ICalendarTokenProtector
{
    string Protect(string refreshToken);

    /// <exception cref="InvalidOperationException">Токен пошкоджений або зашифрований іншим набором ключів.</exception>
    string Unprotect(string protectedValue);
}

public sealed class CalendarTokenProtector(IDataProtectionProvider provider) : ICalendarTokenProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("Lighthouse.Calendar.RefreshToken.v1");

    public string Protect(string refreshToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(refreshToken);
        return _protector.Protect(refreshToken);
    }

    public string Unprotect(string protectedValue)
    {
        ArgumentException.ThrowIfNullOrEmpty(protectedValue);
        try
        {
            return _protector.Unprotect(protectedValue);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException(
                "The stored calendar token cannot be decrypted (data corrupted or key ring changed).", ex);
        }
    }
}