namespace Lighthouse.Mobile.Infrastructure;

/// <summary>Налаштування входу через Google (ідентифікатори клієнта не є секретами).</summary>
internal static class GoogleOAuthConfig
{
    public const string AndroidClientId = "559982102949-po976tvj1b1ub7dm3vm3qjl2075jnmsm.apps.googleusercontent.com";

    /// <summary>Зворотний запис Client ID; значення має збігатися з GoogleOAuth.CallbackScheme(AndroidClientId).</summary>
    public const string CallbackScheme = "com.googleusercontent.apps.559982102949-po976tvj1b1ub7dm3vm3qjl2075jnmsm";

    public const string RedirectUri = CallbackScheme + ":/oauth2redirect";
}