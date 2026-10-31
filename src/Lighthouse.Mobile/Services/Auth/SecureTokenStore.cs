namespace Lighthouse.Mobile.Services.Auth;

public sealed class SecureTokenStore : ITokenStore
{
    private const string AccessKey = "access_token";
    private const string RefreshKey = "refresh_token";

    public Task SaveAsync(string accessToken, string refreshToken) => Task.WhenAll(
        SecureStorage.Default.SetAsync(AccessKey, accessToken),
        SecureStorage.Default.SetAsync(RefreshKey, refreshToken));

    public async Task<(string? AccessToken, string? RefreshToken)> GetAsync()
    {
        var access = await SecureStorage.Default.GetAsync(AccessKey);
        var refresh = await SecureStorage.Default.GetAsync(RefreshKey);
        return (access, refresh);
    }

    public Task ClearAsync()
    {
        SecureStorage.Default.Remove(AccessKey);
        SecureStorage.Default.Remove(RefreshKey);
        return Task.CompletedTask;
    }
}