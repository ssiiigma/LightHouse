using System.Text.Json;
using Lighthouse.Mobile.Data;
using Lighthouse.Mobile.Infrastructure;
using Lighthouse.Mobile.Services.Api;
using Lighthouse.Mobile.Services.Sync;
using Lighthouse.Shared.Contracts.Auth;

namespace Lighthouse.Mobile.Services.Auth;

public sealed record UserProfile(string Name, string Email);

/// <summary>Реєстрація, вхід і вихід; керує сеансом і локальними даними.</summary>
public interface IAuth
{
    /// <summary>Сеанс завершено сервером (refresh-токен недійсний): показати екран входу.</summary>
    event Action? SessionExpired;

    Task<bool> IsAuthenticatedAsync();
    Task<AuthResponse> RegisterAsync(string name, string email, string password, CancellationToken ct = default);
    Task<AuthResponse> LoginAsync(string email, string password, CancellationToken ct = default);

    /// <summary>Вихід: відкликання токена на сервері (за наявності мережі) і очищення локальних даних.</summary>
    Task LogoutAsync(CancellationToken ct = default);

    /// <summary>Ім'я та пошта поточного користувача (з налаштувань, інакше з claim-ів токена доступу).</summary>
    Task<UserProfile> GetProfileAsync();
}

public sealed class AuthManager : IAuth
{
    private readonly IApiClient _api;
    private readonly ISecureStore _secureStore;
    private readonly ILocalDB _localDb;
    private readonly ISyncSvc _sync;

    public event Action? SessionExpired;

    public AuthManager(IApiClient api, ISecureStore secureStore, ILocalDB localDb, ISyncSvc sync)
    {
        _api = api;
        _secureStore = secureStore;
        _localDb = localDb;
        _sync = sync;
        _api.SessionExpired += () =>
        {
            _sync.Stop();
            SessionExpired?.Invoke();
        };
    }

    public async Task<bool> IsAuthenticatedAsync()
    {
        var (_, refreshToken) = await _secureStore.GetAsync();
        return !string.IsNullOrEmpty(refreshToken);
    }

    public async Task<AuthResponse> RegisterAsync(string name, string email, string password, CancellationToken ct = default)
    {
        var auth = await _api.RegisterAsync(name, email, password, ct);
        await OnSignedInAsync(auth);
        return auth;
    }

    public async Task<AuthResponse> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        var auth = await _api.LoginAsync(email, password, ct);
        await OnSignedInAsync(auth);
        return auth;
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        _sync.Stop();

        var (_, refreshToken) = await _secureStore.GetAsync();
        if (!string.IsNullOrEmpty(refreshToken))
        {
            try
            {
                await _api.LogoutAsync(refreshToken, ct);
            }
            catch (Exception)
            {
                // Без мережі токен на сервері не відкликається й діє до закінчення терміну (30 діб).
            }
        }

        await _secureStore.ClearAsync();
        await ClearLocalDataAsync();
    }

    public async Task<UserProfile> GetProfileAsync()
    {
        var name = Preferences.Default.Get(PreferenceKeys.UserName, string.Empty);
        var email = Preferences.Default.Get(PreferenceKeys.UserEmail, string.Empty);

        if (name.Length == 0)
        {
            var (accessToken, _) = await _secureStore.GetAsync();
            if (TryReadClaims(accessToken, out var tokenName, out var tokenEmail))
            {
                name = tokenName;
                email = tokenEmail;
                Preferences.Default.Set(PreferenceKeys.UserName, name);
                Preferences.Default.Set(PreferenceKeys.UserEmail, email);
            }
        }

        return new UserProfile(name, email);
    }

    private async Task OnSignedInAsync(AuthResponse auth)
    {
        // Дані іншого облікового запису на пристрої не лишаємо.
        var previousUser = Preferences.Default.Get(PreferenceKeys.UserId, string.Empty);
        if (previousUser != auth.User.Id.ToString())
        {
            await ClearLocalDataAsync();
        }

        Preferences.Default.Set(PreferenceKeys.UserId, auth.User.Id.ToString());
        Preferences.Default.Set(PreferenceKeys.UserName, auth.User.Name);
        Preferences.Default.Set(PreferenceKeys.UserEmail, auth.User.Email);
        await _secureStore.SaveAsync(auth.AccessToken, auth.RefreshToken);
        _sync.Start();
    }

    private async Task ClearLocalDataAsync()
    {
        await _localDb.ClearAllAsync();
        SyncCursor.ClearAll();
        Preferences.Default.Remove(PreferenceKeys.UserId);
        Preferences.Default.Remove(PreferenceKeys.UserName);
        Preferences.Default.Remove(PreferenceKeys.UserEmail);
    }

    /// <summary>Читає claim-и name і email з корисного навантаження JWT (підпис тут не перевіряється: дані лише для відображення).</summary>
    private static bool TryReadClaims(string? jwt, out string name, out string email)
    {
        name = email = string.Empty;
        var parts = jwt?.Split('.');
        if (parts is not { Length: 3 })
        {
            return false;
        }

        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var document = JsonDocument.Parse(Convert.FromBase64String(payload));
            name = document.RootElement.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
            email = document.RootElement.TryGetProperty("email", out var e) ? e.GetString() ?? string.Empty : string.Empty;
            return name.Length > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}