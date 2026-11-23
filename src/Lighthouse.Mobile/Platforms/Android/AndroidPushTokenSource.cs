using Android.Gms.Extensions;
using Firebase.Messaging;
using Lighthouse.Mobile.Infrastructure;
using Lighthouse.Mobile.Services.Push;

namespace Lighthouse.Mobile;

public sealed class AndroidPushTokenSource : IPushTokenSource
{
    public async Task<string?> GetTokenAsync()
    {
        try
        {
            var token = (await FirebaseMessaging.Instance.GetToken().AsAsync<Java.Lang.String>())?.ToString();
            if (!string.IsNullOrEmpty(token))
            {
                PushTokenStore.Save(token, notify: false);
                return token;
            }
        }
        catch (Exception)
        {
            // Google Play Services недоступні або немає мережі: користуємось збереженим токеном
        }

        return PushTokenStore.Token;
    }
}