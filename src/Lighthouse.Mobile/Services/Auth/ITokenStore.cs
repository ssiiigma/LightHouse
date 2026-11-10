namespace Lighthouse.Mobile.Services.Auth;

public interface ITokenStore
{
    Task SaveAsync(string accessToken, string refreshToken);
    Task<(string? AccessToken, string? RefreshToken)> GetAsync();
    Task ClearAsync();
}