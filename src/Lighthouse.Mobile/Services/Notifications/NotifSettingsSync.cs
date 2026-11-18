using Lighthouse.Mobile.Infrastructure;
using Lighthouse.Mobile.Services.Api;
using Lighthouse.Shared.Contracts.Notifications;
using Microsoft.Extensions.Logging;

namespace Lighthouse.Mobile.Services.Notifications;

/// <summary>Синхронізація налаштувань сповіщень: ненадіслані зміни користувача йдуть на сервер, інакше беруться серверні.</summary>
public interface INotifSettingsSync
{
    void Start();

    /// <summary>Синхронізація з затримкою 1,5 с (зливає серію змін).</summary>
    void RequestSync();

    /// <summary>true — синхронізація виконана (є мережа й обліковий запис).</summary>
    Task<bool> SyncAsync(CancellationToken ct = default);

    /// <summary>Скидає прапорець ненадісланих змін (вихід з облікового запису).</summary>
    void Reset();
}

public sealed class NotifSettingsSync(
    INotifSettings settings, IApiClient api, IConnectivity connectivity, ILogger<NotifSettingsSync> logger)
    : INotifSettingsSync
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _pending;
    private bool _started;

    public void Start()
    {
        if (_started)
        {
            RequestSync();
            return;
        }

        _started = true;
        settings.UserChanged += RequestSync;
        connectivity.ConnectivityChanged += (_, e) =>
        {
            if (e.NetworkAccess == NetworkAccess.Internet)
            {
                RequestSync();
            }
        };
        RequestSync();
    }

    public void RequestSync()
    {
        var cts = new CancellationTokenSource();
        Interlocked.Exchange(ref _pending, cts)?.Cancel();

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1500, cts.Token);
                await SyncAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // замінено новішим запитом
            }
        });
    }

    public async Task<bool> SyncAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (Preferences.Default.Get(PreferenceKeys.UserId, string.Empty).Length == 0
                || connectivity.NetworkAccess != NetworkAccess.Internet)
            {
                return false;
            }

            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(TimeSpan.FromSeconds(10));

            if (settings.IsDirty)
            {
                var revision = settings.Revision;
                var dto = settings.ToDto();
                await api.PutAsync<NotificationSettingsDto, NotificationSettingsDto>(
                    "api/notification-settings", dto, limit.Token);
                settings.MarkClean(revision);
            }
            else
            {
                settings.ApplyFromServer(
                    await api.GetAsync<NotificationSettingsDto>("api/notification-settings", limit.Token));
            }

            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex, "Notification settings were not synchronized.");
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Reset() => settings.ResetSyncState();
}