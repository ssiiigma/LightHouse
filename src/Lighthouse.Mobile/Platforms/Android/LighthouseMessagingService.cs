using Android.App;
using Firebase.Messaging;
using Lighthouse.Mobile.Infrastructure;

namespace Lighthouse.Mobile;

/// <summary>Приймає push від сервера (також коли застосунок закрито) і показує сповіщення мовою інтерфейсу.</summary>
[Service(Exported = false)]
[IntentFilter(new[] { "com.google.firebase.MESSAGING_EVENT" })]
public class LighthouseMessagingService : FirebaseMessagingService
{
    public override void OnNewToken(string token)
    {
        base.OnNewToken(token);
        PushTokenStore.Save(token, notify: true);
    }

    public override void OnMessageReceived(RemoteMessage message)
    {
        base.OnMessageReceived(message);
        PushNotificationPresenter.Show(this, message.Data);
    }
}