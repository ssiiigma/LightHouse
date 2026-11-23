using Lighthouse.Mobile.Infrastructure;
using Lighthouse.Mobile.Services.Api;
using Lighthouse.Shared.Contracts.Notifications;
using Lighthouse.Shared.Notifications;
using Microsoft.Extensions.Logging;

namespace Lighthouse.Mobile.Services.Push;

/// <summary>Джерело токена FCM (реалізація для Android).</summary>
public interface IPushTokenSource
{
    Task<string?> GetTokenAsync();
}

/// <summary>Реєстрація пристрою для серверних push-сповіщень (щоденний огляд, розумні нагадування).</summary>
public interface IPushSvc
{
    void Start();

    /// <summary>Реєструє поточний токен на сервері (з невеликою затримкою; повторні виклики зливаються).</summary>
    void RequestRegister();

    /// <summary>Видаляє реєстрацію пристрою на сервері (перед виходом з облікового запису).</summary>
    Task UnregisterAsync();

    Task<int> SendTestAsync(CancellationToken ct);
}

public sealed class PushService(
    IPushTokenSource source, IApiClient api, IConnectivity connectivity, ILogger<PushService> logger) : IPushSvc
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _pending;
    private bool _started;

    public void Start()
    {
        if (_started)
        {
            RequestRegister();
            return;
        }

        _started = true;
        PushTokenStore.Changed += RequestRegister;
        connectivity.ConnectivityChanged += (_, e) =>
        {
            if (e.NetworkAccess == NetworkAccess.Internet)
            {
                RequestRegister();
            }
        };
        RequestRegister();
    }

    public void RequestRegister()
    {
        var cts = new CancellationTokenSource();
        Interlocked.Exchange(ref _pending, cts)?.Cancel();

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1000, cts.Token);
                await RegisterAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // замінено новішим запитом
            }
        });
    }

    public async Task UnregisterAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var token = PushTokenStore.Token;
            if (token is null || connectivity.NetworkAccess != NetworkAccess.Internet)
            {
                return;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await api.DeleteAsync($"api/devices?token={Uri.EscapeDataString(token)}", cts.Token);
            PushTokenStore.Registered = string.Empty;
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex, "The device was not unregistered.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<int> SendTestAsync(CancellationToken ct) =>
        SendTestCoreAsync(ct);

    private async Task<int> SendTestCoreAsync(CancellationToken ct)
    {
        await RegisterAsync(ct); // гарантуємо, що токен цього пристрою відомий серверу
        return (await api.PostAsync<object, NotificationPlanner.PushTestResultDto>("api/devices/test", new { }, ct)).Sent;
    }

    private async Task RegisterAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var userId = Preferences.Default.Get(PreferenceKeys.UserId, string.Empty);
            if (userId.Length == 0 || connectivity.NetworkAccess != NetworkAccess.Internet)
            {
                return;
            }

            var token = await source.GetTokenAsync();
            if (string.IsNullOrEmpty(token) || PushTokenStore.Registered == $"{userId}|{token}")
            {
                return;
            }

            await api.PostNoContentAsync("api/devices", new RegisterDeviceRequest(token), ct);
            PushTokenStore.Registered = $"{userId}|{token}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogInformation(ex, "The device was not registered for push notifications.");
        }
        finally
        {
            _gate.Release();
        }
    }
}