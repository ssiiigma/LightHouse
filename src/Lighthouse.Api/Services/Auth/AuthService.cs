using Lighthouse.Api.Data;
using Lighthouse.Api.Infrastructure;
using Lighthouse.Core.Entities;
using Lighthouse.Shared.Contracts.Auth;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace Lighthouse.Api.Services.Auth;

public sealed class AuthService(
    LighthouseDbContext db, ITokenService tokens, IAccountService accounts, ILogger<AuthService> logger, IGoogleTokenValidator google)
    : IAuthService
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
        };
        user.NotificationSettings = new NotificationSettings { UserId = user.Id };

        db.Users.Add(user);
        var response = await IssueTokensAsync(user, ct); // користувач і refresh-токен зберігаються однією транзакцією

        try
        {
            await accounts.SendConfirmationAsync(user, request.Language, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The confirmation email was not queued."); // реєстрація від цього не залежить
        }

        return response;
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
    
        public Task<AuthResponse> GoogleLoginAsync(GoogleLoginRequest request, CancellationToken ct) =>
        GoogleLoginCoreAsync(request, retryOnConflict: true, ct);

    private async Task<AuthResponse> GoogleLoginCoreAsync(GoogleLoginRequest request, bool retryOnConflict, CancellationToken ct)
    {
        var identity = await google.ValidateAsync(request.IdToken, ct)
                       ?? throw new AppException(StatusCodes.Status401Unauthorized, "Invalid Google token.");

        if (!identity.EmailVerified || string.IsNullOrWhiteSpace(identity.Email))
        {
            throw new AppException(StatusCodes.Status400BadRequest, "The email of the Google account is not verified.");
        }

        var email = identity.Email.Trim().ToLowerInvariant();
        var now = DateTime.UtcNow;

        // 1) вже прив'язаний акаунт; 2) акаунт з таким підтвердженим e-mail (прив'язуємо); 3) новий акаунт.
        var user = await db.Users.FirstOrDefaultAsync(u => u.GoogleId == identity.Subject && !u.IsDeleted, ct)
                   ?? await db.Users.FirstOrDefaultAsync(u => u.Email == email && !u.IsDeleted, ct);

        if (user is not null)
        {
            if (!string.IsNullOrEmpty(user.GoogleId) && user.GoogleId != identity.Subject)
            {
                throw new AppException(StatusCodes.Status409Conflict, "This email is already linked to another Google account.");
            }

            user.GoogleId = identity.Subject;
            user.IsEmailConfirmed = true;
            user.UpdatedAtUtc = now;
            user.ServerModifiedAtUtc = now;
            return await IssueTokensAsync(user, ct);
        }

        var name = string.IsNullOrWhiteSpace(identity.Name) ? email.Split('@')[0] : identity.Name.Trim();
        user = new User
        {
            Name = name.Length <= 100 ? name : name[..100],
            Email = email,
            // Пароль недоступний: входити можна через Google або після скидання пароля.
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))),
            GoogleId = identity.Subject,
            IsEmailConfirmed = true
        };
        db.Users.Add(user);

        try
        {
            return await IssueTokensAsync(user, ct);
        }
        catch (DbUpdateException) when (retryOnConflict)
        {
            // Паралельний перший вхід створив того самого користувача: повторюємо як вхід у наявний акаунт.
            db.ChangeTracker.Clear();
            return await GoogleLoginCoreAsync(request, retryOnConflict: false, ct);
        }
    }
}
