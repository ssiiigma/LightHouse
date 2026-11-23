using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Lighthouse.Api.Services.Calendar;

/// <summary>Параметр state для повернення з Google: підписаний, шифрований і дійсний 10 хвилин; містить лише ідентифікатор користувача.</summary>
public sealed class CalendarStateProtector(IDataProtectionProvider provider)
{
    private readonly ITimeLimitedDataProtector _protector =
        provider.CreateProtector("Lighthouse.CalendarOAuthState").ToTimeLimitedDataProtector();

    public string Create(Guid userId, TimeSpan? lifetime = null) =>
        _protector.Protect(userId.ToString("N"), lifetime ?? TimeSpan.FromMinutes(10));

    /// <returns>null, якщо значення підроблене, пошкоджене або прострочене.</returns>
    public Guid? Read(string state)
    {
        try
        {
            return Guid.ParseExact(_protector.Unprotect(state), "N");
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }
}