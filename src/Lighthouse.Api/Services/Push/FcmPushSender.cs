using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Lighthouse.Api.Options;
using Microsoft.Extensions.Options;
using AppOptions = FirebaseAdmin.AppOptions;

namespace Lighthouse.Api.Services.Push;

/// <summary>Відправлення data-повідомлень через Firebase Cloud Messaging (HTTP v1). Без ключа push вимкнено.</summary>
public sealed class FcmPushSender : IPushSender
{
    private readonly FirebaseMessaging? _messaging;
    private readonly ILogger<FcmPushSender> _logger;

    public FcmPushSender(IOptions<FirebaseOptions> options, ILogger<FcmPushSender> logger)
    {
        _logger = logger;
        var path = options.Value.ServiceAccountPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            logger.LogWarning("Firebase:ServiceAccountPath is not configured; push notifications are disabled.");
            return;
        }

        try
        {
            var app = FirebaseApp.DefaultInstance
                      ?? FirebaseApp.Create(new AppOptions { Credential = GoogleCredential.FromFile(path) });
            _messaging = FirebaseMessaging.GetMessaging(app);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The Firebase service account key could not be loaded; push notifications are disabled.");
        }
    }

    public bool IsConfigured => _messaging is not null;

    public async Task<PushResult> SendAsync(string deviceToken, IReadOnlyDictionary<string, string> data, CancellationToken ct)
    {
        if (_messaging is null)
        {
            return PushResult.Disabled;
        }

        try
        {
            await _messaging.SendAsync(new Message
            {
                Token = deviceToken,
                Data = data,
                Android = new AndroidConfig { Priority = Priority.High, TimeToLive = TimeSpan.FromHours(6) }
            }, ct);
            return PushResult.Sent;
        }
        catch (FirebaseMessagingException ex)
            when (ex.MessagingErrorCode is MessagingErrorCode.Unregistered or MessagingErrorCode.SenderIdMismatch)
        {
            return PushResult.Unregistered;
        }
        catch (FirebaseMessagingException ex)
        {
            _logger.LogWarning("FCM rejected a message: {Code}.", ex.MessagingErrorCode);
            return PushResult.Failed;
        }
    }
}