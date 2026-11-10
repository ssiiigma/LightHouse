using System.Net.Http.Headers;
using System.Net.Http.Json;
using Lighthouse.Mobile.Services.Auth;
using Lighthouse.Shared.Contracts.Auth;

namespace Lighthouse.Mobile.Services.Api;

public sealed class ApiClient(HttpClient http, ITokenStore tokenStore)
{
    public async Task<AuthResponse> RegisterAsync(string name, string email, string password, CancellationToken ct)
    {
        var response = await http.PostAsJsonAsync("api/auth/register", new RegisterRequest(name, email, password), ct);
        return await ReadAuthResponseAsync(response, ct);
    }

    public async Task<AuthResponse> LoginAsync(string email, string password, CancellationToken ct)
    {
        var response = await http.PostAsJsonAsync("api/auth/login", new LoginRequest(email, password), ct);
        return await ReadAuthResponseAsync(response, ct);
    }

    public async Task<HttpResponseMessage> SendAuthorizedAsync(
        Func<HttpRequestMessage> requestFactory, CancellationToken ct)
    {
        var (accessToken, _) = await tokenStore.GetAsync();
        var response = await SendWithTokenAsync(requestFactory(), accessToken, ct);

        if (response.StatusCode != System.Net.HttpStatusCode.Unauthorized)
        {
            return response;
        }

        var (_, refreshToken) = await tokenStore.GetAsync();
        if (string.IsNullOrEmpty(refreshToken))
        {
            return response;
        }

        var refreshResponse = await http.PostAsJsonAsync("api/auth/refresh", new RefreshRequest(refreshToken), ct);
        if (!refreshResponse.IsSuccessStatusCode)
        {
            await tokenStore.ClearAsync();
            return response;
        }

        var auth = await refreshResponse.Content.ReadFromJsonAsync<AuthResponse>(cancellationToken: ct)
                   ?? throw new InvalidOperationException("Empty refresh response.");
        await tokenStore.SaveAsync(auth.AccessToken, auth.RefreshToken);

        return await SendWithTokenAsync(requestFactory(), auth.AccessToken, ct);
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
        if (!response.IsSuccessStatusCode)
        {
            var problem = await response.Content.ReadAsStringAsync(ct);
            throw new ApiException(response.StatusCode, problem);
        }

        return await response.Content.ReadFromJsonAsync<AuthResponse>(cancellationToken: ct)
               ?? throw new InvalidOperationException("Empty auth response.");
    }
}

public sealed class ApiException(System.Net.HttpStatusCode statusCode, string details)
    : Exception($"API error {(int)statusCode}: {details}")
{
    public System.Net.HttpStatusCode StatusCode { get; } = statusCode;
}