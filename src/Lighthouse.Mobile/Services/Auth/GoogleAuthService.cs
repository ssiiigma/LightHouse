using System.Text.Json;
using Lighthouse.Mobile.Infrastructure;
using Lighthouse.Shared.Auth;
using Microsoft.Extensions.Logging;

namespace Lighthouse.Mobile.Services.Auth;

public sealed class GoogleAuthException(string message) : Exception(message);

/// <summary>Отримання ID-токена Google через браузер (OAuth 2.0, код авторизації + PKCE).</summary>
public interface IGoogleAuth
{
    /// <returns>ID-токен або null, якщо користувач скасував вхід.</returns>
    Task<string?> GetIdTokenAsync(CancellationToken ct);
}

public sealed class GoogleAuthService(ILogger<GoogleAuthService> logger) : IGoogleAuth
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public async Task<string?> GetIdTokenAsync(CancellationToken ct)
    {
        var verifier = Pkce.CreateVerifier();
        var state = Pkce.CreateState();
        var url = GoogleOAuth.BuildAuthorizationUrl(
            GoogleOAuthConfig.AndroidClientId, GoogleOAuthConfig.RedirectUri, Pkce.CreateChallenge(verifier), state);

        WebAuthenticatorResult result;
        try
        {
            result = await WebAuthenticator.Default.AuthenticateAsync(new WebAuthenticatorOptions
            {
                Url = new Uri(url),
                CallbackUrl = new Uri(GoogleOAuthConfig.RedirectUri)
            });
        }
        catch (OperationCanceledException)
        {
            return null; // користувач закрив вікно входу
        }

        if (result.Properties.TryGetValue("error", out var error))
        {
            if (error == "access_denied")
            {
                return null;
            }

            throw new GoogleAuthException($"Google returned an error: {error}");
        }

        if (!result.Properties.TryGetValue("state", out var returnedState) || returnedState != state)
        {
            throw new GoogleAuthException("The state parameter does not match.");
        }

        if (!result.Properties.TryGetValue("code", out var code) || string.IsNullOrEmpty(code))
        {
            throw new GoogleAuthException("Google did not return an authorization code.");
        }

        return await ExchangeAsync(code, verifier, ct);
    }

    private async Task<string> ExchangeAsync(string code, string verifier, CancellationToken ct)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = GoogleOAuthConfig.AndroidClientId,
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = GoogleOAuthConfig.RedirectUri
        });

        using var response = await Http.PostAsync(GoogleOAuth.TokenEndpoint, content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Google token exchange failed with status {Status}.", (int)response.StatusCode);
            throw new GoogleAuthException("The token exchange failed.");
        }

        using var document = JsonDocument.Parse(body);
        if (document.RootElement.TryGetProperty("id_token", out var token) && token.GetString() is { Length: > 0 } idToken)
        {
            return idToken;
        }

        throw new GoogleAuthException("Google did not return an ID token.");
    }
}