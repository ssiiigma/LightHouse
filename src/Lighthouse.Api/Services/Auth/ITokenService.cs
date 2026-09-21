using Lighthouse.Core.Entities;

namespace Lighthouse.Api.Services.Auth;

public sealed record RefreshTokenInfo(string Token, string Hash, DateTime ExpiresAtUtc);

public interface ITokenService
{
    (string Token, DateTime ExpiresAtUtc) CreateAccessToken(User user);
    RefreshTokenInfo CreateRefreshToken();
    string Hash(string token);
}
