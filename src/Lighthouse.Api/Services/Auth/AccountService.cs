using System.Security.Cryptography;
using Lighthouse.Api.Data;
using Lighthouse.Api.Infrastructure;
using Lighthouse.Api.Options;
using Lighthouse.Api.Services.Email;
using Lighthouse.Core.Entities;
using Lighthouse.Shared.Enums;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Lighthouse.Api.Services.Auth;

public interface IAccountService
{
    Task SendConfirmationAsync(User user, string? language, CancellationToken ct);

    /// <summary>Ставить лист зі скиданням пароля в чергу, якщо обліковий запис існує; інакше нічого не робить (без розкриття).</summary>
    Task RequestPasswordResetAsync(string email, string? language, CancellationToken ct);

    Task ResetPasswordAsync(string token, string newPassword, CancellationToken ct);
    Task ConfirmEmailAsync(string token, CancellationToken ct);
}

public sealed class AccountService(
    LighthouseDbContext db, ITokenService tokens, IEmailQueue queue, IOptions<AppOptions> app) : IAccountService
{
    /// <summary>Строк дії одноразових посилань: 24 години.</summary>
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(24);

    /// <summary>Не частіше одного листа скидання на хвилину для одного користувача (захист від спаму).</summary>
    private static readonly TimeSpan ResetCooldown = TimeSpan.FromSeconds(60);

    private string BaseUrl => app.Value.PublicBaseUrl.TrimEnd('/');

    public async Task SendConfirmationAsync(User user, string? language, CancellationToken ct)
    {
        if (user.IsEmailConfirmed)
        {
            return;
        }

        var token = await IssueAsync(user.Id, EmailTokenPurpose.Confirm, ct);
        var link = $"{BaseUrl}/api/auth/confirm-email?token={Uri.EscapeDataString(token)}";
        queue.TryEnqueue(EmailTemplates.Confirmation(user.Email, user.Name, link, language));
    }

    public async Task RequestPasswordResetAsync(string email, string? language, CancellationToken ct)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalized && !u.IsDeleted, ct);
        if (user is null)
        {
            return;
        }

        var since = DateTime.UtcNow - ResetCooldown;
        if (await db.EmailTokens.AnyAsync(
                t => t.UserId == user.Id && t.Purpose == EmailTokenPurpose.Reset && t.CreatedAtUtc > since, ct))
        {
            return;
        }

        var token = await IssueAsync(user.Id, EmailTokenPurpose.Reset, ct);
        var link = $"{BaseUrl}/reset-password?token={Uri.EscapeDataString(token)}";
        queue.TryEnqueue(EmailTemplates.PasswordReset(user.Email, user.Name, token, link, language));
    }

    public async Task ResetPasswordAsync(string token, string newPassword, CancellationToken ct)
    {
        var record = await FindValidAsync(token, EmailTokenPurpose.Reset, ct) ?? throw Invalid();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == record.UserId && !u.IsDeleted, ct) ?? throw Invalid();

        var now = DateTime.UtcNow;
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        user.IsEmailConfirmed = true; // доступ до пошти підтверджено самим фактом переходу за посиланням
        user.UpdatedAtUtc = now;
        user.ServerModifiedAtUtc = now;
        record.UsedAtUtc = now;

        // Усі наявні сеанси втрачають чинність: хто знав старий пароль, більше не має доступу.
        foreach (var refresh in await db.RefreshTokens.Where(r => r.UserId == user.Id && r.RevokedAtUtc == null).ToListAsync(ct))
        {
            refresh.RevokedAtUtc = now;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task ConfirmEmailAsync(string token, CancellationToken ct)
    {
        var record = await FindValidAsync(token, EmailTokenPurpose.Confirm, ct) ?? throw Invalid();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == record.UserId && !u.IsDeleted, ct) ?? throw Invalid();

        var now = DateTime.UtcNow;
        user.IsEmailConfirmed = true;
        user.UpdatedAtUtc = now;
        user.ServerModifiedAtUtc = now;
        record.UsedAtUtc = now;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Створює одноразовий токен (128 біт); попередні невикористані токени того ж призначення втрачають чинність.</summary>
    private async Task<string> IssueAsync(Guid userId, EmailTokenPurpose purpose, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        foreach (var old in await db.EmailTokens
                     .Where(t => t.UserId == userId && t.Purpose == purpose && t.UsedAtUtc == null)
                     .ToListAsync(ct))
        {
            old.UsedAtUtc = now;
        }

        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(16));
        db.EmailTokens.Add(new EmailToken
        {
            UserId = userId,
            TokenHash = tokens.Hash(token),
            Purpose = purpose,
            CreatedAtUtc = now,
            ExpiresAtUtc = now + TokenLifetime
        });

        await db.SaveChangesAsync(ct);
        return token;
    }

    private async Task<EmailToken?> FindValidAsync(string token, EmailTokenPurpose purpose, CancellationToken ct)
    {
        var hash = tokens.Hash(token.Trim());
        var now = DateTime.UtcNow;
        return await db.EmailTokens.FirstOrDefaultAsync(
            t => t.TokenHash == hash && t.Purpose == purpose && t.UsedAtUtc == null && t.ExpiresAtUtc > now, ct);
    }

    private static AppException Invalid() => new(StatusCodes.Status400BadRequest, "Invalid or expired token.");
}