using System.Security.Claims;

namespace Lighthouse.Api.Infrastructure;

/// <summary>Назви claim-ів, що використовуються в JWT.</summary>
public static class TokenClaims
{
    public const string Subject = "sub";
    public const string Email = "email";
    public const string Name = "name";
}

public static class ClaimsPrincipalExtensions
{
    /// <summary>Ідентифікатор поточного користувача з JWT (claim "sub").</summary>
    public static Guid GetUserId(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(TokenClaims.Subject)
                    ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(value, out var id)
            ? id
            : throw new AppException(StatusCodes.Status401Unauthorized, "Invalid access token.");
    }
}
