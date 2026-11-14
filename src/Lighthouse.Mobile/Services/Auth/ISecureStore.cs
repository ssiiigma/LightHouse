namespace Lighthouse.Mobile.Services.Auth;

/// <summary>Захищене сховище токенів (Android Keystore через SecureStorage).</summary>
public interface ISecureStore
{
    Task SaveAsync(string accessToken, string refreshToken);
    Task<(string? AccessToken, string? RefreshToken)> GetAsync();
    Task ClearAsync();
}

// Клас названо SecureStorageService, бо SecureStorage — ім'я типу MAUI.
public sealed class SecureStorageService : ISecureStore
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