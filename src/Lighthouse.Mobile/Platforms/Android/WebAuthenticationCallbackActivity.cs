using Android.App;
using Android.Content;
using Android.Content.PM;
using Lighthouse.Mobile.Infrastructure;
using Microsoft.Maui.Authentication;

namespace Lighthouse.Mobile;

/// <summary>Приймає повернення з браузера після входу Google (схема = зворотний Client ID).</summary>
[Activity(NoHistory = true, LaunchMode = LaunchMode.SingleTop, Exported = true)]
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = GoogleOAuthConfig.CallbackScheme)]
public class WebAuthenticationCallbackActivity : WebAuthenticatorCallbackActivity
{
}