using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Lighthouse.Mobile.Services.Auth;
using Lighthouse.Shared.Contracts.Auth;

namespace Lighthouse.Mobile.Services.Api;

/// <summary>Клієнт REST API: підстановка JWT, автоматичне оновлення токена при 401, помилки у вигляді <see cref="ApiException"/>.</summary>
public interface IApiClient
{
    /// <summary>Виникає, коли refresh-токен відхилено сервером: потрібен повторний вхід.</summary>
    event Action? SessionExpired;

    Task<AuthResponse> RegisterAsync(string name, string email, string password, CancellationToken ct);
    Task<AuthResponse> LoginAsync(string email, string password, CancellationToken ct);
    Task LogoutAsync(string refreshToken, CancellationToken ct);
    Task<TResponse> GetAsync<TResponse>(string path, CancellationToken ct);
    Task<TResponse> PutAsync<TRequest, TResponse>(string path, TRequest body, CancellationToken ct);
    Task DeleteAsync(string path, CancellationToken ct);
}

public sealed class ApiClient(HttpClient http, ISecureStore secureStore) : IApiClient
{
    // Паралельні запити з простроченим токеном оновлюють його по одному: refresh-токен одноразовий (ротація).
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public event Action? SessionExpired;

    public async Task<AuthResponse> RegisterAsync(string name, string email, string password, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("api/auth/register", new RegisterRequest(name, email, password), ct);
        return await ReadAuthResponseAsync(response, ct);
    }

    public async Task<AuthResponse> LoginAsync(string email, string password, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("api/auth/login", new LoginRequest(email, password), ct);
        return await ReadAuthResponseAsync(response, ct);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("api/auth/logout", new LogoutRequest(refreshToken), ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task<TResponse> GetAsync<TResponse>(string path, CancellationToken ct)
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, path), ct);
        await EnsureSuccessAsync(response, ct);
        return await ReadBodyAsync<TResponse>(response, ct);
    }

    public async Task<TResponse> PutAsync<TRequest, TResponse>(string path, TRequest body, CancellationToken ct)
    {
        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Put, path) { Content = JsonContent.Create(body) }, ct);
        await EnsureSuccessAsync(response, ct);
        return await ReadBodyAsync<TResponse>(response, ct);
    }

    public async Task DeleteAsync(string path, CancellationToken ct)
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Delete, path), ct);
        await EnsureSuccessAsync(response, ct);
    }

    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> requestFactory, CancellationToken ct)
    {
        var (accessToken, _) = await secureStore.GetAsync();
        var response = await SendWithTokenAsync(requestFactory(), accessToken, ct);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        var renewed = await RefreshAsync(accessToken ?? string.Empty, ct)
                      ?? throw new ApiException(HttpStatusCode.Unauthorized, "Session expired.");

        return await SendWithTokenAsync(requestFactory(), renewed, ct);
    }

    private async Task<string?> RefreshAsync(string failedAccessToken, CancellationToken ct)
    {
        await _refreshGate.WaitAsync(ct);
        try
        {
            var (accessToken, refreshToken) = await secureStore.GetAsync();

            // Інший запит уже оновив токен, поки ми чекали.
            if (!string.IsNullOrEmpty(accessToken) && accessToken != failedAccessToken)
            {
                return accessToken;
            }

            if (string.IsNullOrEmpty(refreshToken))
            {
                return null;
            }

            using var response = await http.PostAsJsonAsync("api/auth/refresh", new RefreshRequest(refreshToken), ct);
            if (!response.IsSuccessStatusCode)
            {
                // Токени стираємо лише коли сервер явно їх відхилив; збій сервера/мережі не завершує сеанс.
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    await secureStore.ClearAsync();
                    SessionExpired?.Invoke();
                }

                return null;
            }

            var auth = await ReadBodyAsync<AuthResponse>(response, ct);
            await secureStore.SaveAsync(auth.AccessToken, auth.RefreshToken);
            return auth.AccessToken;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task<HttpResponseMessage> SendWithTokenAsync(HttpRequestMessage request, string? token, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await http.SendAsync(request, ct);
    }

    private static async Task<AuthResponse> ReadAuthResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await EnsureSuccessAsync(response, ct);
        return await ReadBodyAsync<AuthResponse>(response, ct);
    }

    private static async Task<T> ReadBodyAsync<T>(HttpResponseMessage response, CancellationToken ct) =>
        await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct)
        ?? throw new InvalidOperationException("Empty response body.");

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new ApiException(response.StatusCode, await response.Content.ReadAsStringAsync(ct));
        }
    }
}

public sealed class ApiException(HttpStatusCode statusCode, string details)
    : Exception($"API error {(int)statusCode}: {details}")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}