namespace Lighthouse.Shared.Auth;

/// <summary>Параметри OAuth 2.0 для входу через Google (Android-клієнт із власною URI-схемою).</summary>
public static class GoogleOAuth
{
    public const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    public const string TokenEndpoint = "https://oauth2.googleapis.com/token";

    private const string ClientIdSuffix = ".apps.googleusercontent.com";

    /// <summary>Схема повернення: Client ID у зворотному записі (вимога Google для Android-клієнтів).</summary>
    public static string CallbackScheme(string androidClientId)
    {
        var id = androidClientId.Trim();
        if (id.EndsWith(ClientIdSuffix, StringComparison.OrdinalIgnoreCase))
        {
            id = id[..^ClientIdSuffix.Length];
        }

        return "com.googleusercontent.apps." + id;
    }

    public static string RedirectUri(string androidClientId) => CallbackScheme(androidClientId) + ":/oauth2redirect";

    public static string BuildAuthorizationUrl(string clientId, string redirectUri, string codeChallenge, string state)
    {
        var parameters = new (string Name, string Value)[]
        {
            ("client_id", clientId),
            ("redirect_uri", redirectUri),
            ("response_type", "code"),
            ("scope", "openid email profile"),
            ("code_challenge", codeChallenge),
            ("code_challenge_method", "S256"),
            ("state", state),
            ("prompt", "select_account")
        };

        return AuthorizationEndpoint + "?" +
               string.Join("&", parameters.Select(p => $"{p.Name}={Uri.EscapeDataString(p.Value)}"));
    }
}