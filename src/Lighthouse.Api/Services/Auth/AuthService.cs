using Lighthouse.Api.Data;
using Lighthouse.Api.Infrastructure;
using Lighthouse.Core.Entities;
using Lighthouse.Shared.Contracts.Auth;
using Microsoft.EntityFrameworkCore;

namespace Lighthouse.Api.Services.Auth;

public sealed class AuthService(LighthouseDbContext db, ITokenService tokens) : IAuthService
{
    private const int MaxFailedAttempts = 5;                            // FR-8
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var email = NormalizeEmail(request.Email);

        if (await db.Users.AnyAsync(u => u.Email == email, ct))
        {
            throw new AppException(StatusCodes.Status409Conflict, "Email is already registered.");
        }

        var user = new User
        {
            Name = request.Name.Trim(),
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password)
            // TODO (FR-4): надіслати лист підтвердження та виставляти IsEmailConfirmed після переходу за посиланням.
        };
        user.NotificationSettings = new NotificationSettings { UserId = user.Id };

        db.Users.Add(user);
        return await IssueTokensAsync(user, ct); // користувач і refresh-токен зберігаються однією транзакцією
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        const string invalidCredentials = "Invalid email or password.";
        var email = NormalizeEmail(request.Email);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email && !u.IsDeleted, ct);
        if (user is null || user.PasswordHash is null)
        {
            throw new AppException(StatusCodes.Status401Unauthorized, invalidCredentials);
        }

        if (user.LockoutEndUtc is { } lockoutEnd && lockoutEnd > DateTime.UtcNow)
        {
            throw new AppException(StatusCodes.Status423Locked,
                $"Account is locked until {lockoutEnd:u}. Try again later.");
        }

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedAttempts)
            {
                user.LockoutEndUtc = DateTime.UtcNow + LockoutDuration;
                user.FailedLoginCount = 0;
            }

            await db.SaveChangesAsync(ct);
            throw new AppException(StatusCodes.Status401Unauthorized, invalidCredentials);
        }

        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        return await IssueTokensAsync(user, ct);
    }

    public async Task<AuthResponse> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var hash = tokens.Hash(refreshToken);
        var stored = await db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (stored is null || stored.RevokedAtUtc is not null || stored.ExpiresAtUtc <= DateTime.UtcNow)
        {
            throw new AppException(StatusCodes.Status401Unauthorized, "Invalid refresh token.");
        }

        stored.RevokedAtUtc = DateTime.UtcNow; // ротація: старий токен більше не діє
        return await IssueTokensAsync(stored.User, ct);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct)
    {
        var hash = tokens.Hash(refreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (stored is not null && stored.RevokedAtUtc is null)
        {
            stored.RevokedAtUtc = DateTime.UtcNow; // FR-13
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<AuthResponse> IssueTokensAsync(User user, CancellationToken ct)
    {
        var (accessToken, accessExpiresAt) = tokens.CreateAccessToken(user);
        var refresh = tokens.CreateRefreshToken();

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refresh.Hash,
            ExpiresAtUtc = refresh.ExpiresAtUtc
        });

        await db.SaveChangesAsync(ct);

        return new AuthResponse(accessToken, refresh.Token, accessExpiresAt,
            new UserDto(user.Id, user.Name, user.Email));
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
