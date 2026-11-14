using System.Net;
using Lighthouse.Mobile.Data;
using Microsoft.Extensions.Logging;
using Lighthouse.Mobile.Services.Api;

namespace Lighthouse.Mobile.Services.Sync;

public sealed class SyncService : ISyncSvc
{
    private const int InitialRetrySeconds = 10;
    private const int MaxRetrySeconds = 300;
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(1);

    private readonly ILocalDB _localDb;
    private readonly IReadOnlyList<IEntitySyncHandler> _handlers;
    private readonly IConnectivity _connectivity;
    private readonly ILogger<SyncService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private CancellationTokenSource? _scheduled;
    private int _retrySeconds = InitialRetrySeconds;
    private bool _started;

    public SyncService(
        ILocalDB localDb,
        IEnumerable<IEntitySyncHandler> handlers,
        IConnectivity connectivity,
        ILogger<SyncService> logger)
    {
        _localDb = localDb;
        _handlers = handlers.OrderBy(h => h.PullOrder).ToList();
        _connectivity = connectivity;
        _logger = logger;
    }

    public SyncStatus Status { get; private set; } = SyncStatus.Idle;

    public event Action<SyncStatus>? StatusChanged;

    public event Action? DataChanged;

    public void Start()
    {
        if (_started)
        {
            RequestSync();
            return;
        }

        _started = true;
        _connectivity.ConnectivityChanged += OnConnectivityChanged;

        _ = Task.Run(async () =>
        {
            await ResetParkedAsync();
            await SyncNowAsync();
        });
    }

    public void Stop()
    {
        _started = false;
        _connectivity.ConnectivityChanged -= OnConnectivityChanged;
        CancelScheduled();
        SetStatus(SyncStatus.Idle);
    }

    public void RequestSync() => Schedule(Debounce);

    public async Task<int> GetPendingCountAsync()
    {
        var db = await _localDb.GetConnectionAsync();
        return await db.Table<OutboxItem>().CountAsync();
    }

    public async Task<bool> SyncNowAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_connectivity.NetworkAccess != NetworkAccess.Internet)
            {
                SetStatus(SyncStatus.Offline);
                return false;
            }

            SetStatus(SyncStatus.Syncing);

            var changed = await PushAllAsync(ct);
            foreach (var handler in _handlers)
            {
                changed += await handler.PullAsync(ct);
            }

            _retrySeconds = InitialRetrySeconds;
            SetStatus(SyncStatus.Idle);
            if (changed > 0)
            {
                DataChanged?.Invoke();
            }

            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            SetStatus(SyncStatus.Idle);
            return false;
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning("Synchronization stopped: the session has expired.");
            SetStatus(SyncStatus.Error);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Synchronization failed; will retry in {Seconds} s.", _retrySeconds);
            SetStatus(_connectivity.NetworkAccess == NetworkAccess.Internet ? SyncStatus.Error : SyncStatus.Offline);
            ScheduleRetry();
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<int> PushAllAsync(CancellationToken ct)
    {
        var db = await _localDb.GetConnectionAsync();
        var pushed = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var item = await db.Table<OutboxItem>()
                .Where(i => i.Attempts < OutboxStore.MaxAttempts)
                .OrderBy(i => i.Id)
                .FirstOrDefaultAsync();
            if (item is null)
            {
                return pushed;
            }

            var handler = _handlers.FirstOrDefault(h => h.EntityType == item.EntityType);
            if (handler is null)
            {
                await db.DeleteAsync(item);
                continue;
            }

            try
            {
                await handler.PushAsync(item, ct);
                await db.Table<OutboxItem>().DeleteAsync(i => i.Id == item.Id);
                pushed++;
            }
            catch (ApiException ex) when (IsPermanent(ex))
            {
                // Сервер відхилив саме цю зміну (400, 404, 409...): не блокуємо чергу, змінa лишається на пристрої.
                _logger.LogWarning("Server rejected {Type} {Id}: {Status}.", item.EntityType, item.EntityId, ex.StatusCode);
                item.Attempts = OutboxStore.MaxAttempts;
                item.LastError = ex.Message;
                await db.UpdateAsync(item);
                await handler.MarkFailedAsync(item);
            }
            catch (ApiException ex) when ((int)ex.StatusCode >= 500)
            {
                item.Attempts++;
                item.LastError = ex.Message;
                await db.UpdateAsync(item);
                throw;
            }
        }
    }

    /// <summary>Помилки 4xx, повторення яких нічого не змінить (крім 401 — потрібен вхід, 408 і 429 — тимчасові).</summary>
    private static bool IsPermanent(ApiException ex) =>
        (int)ex.StatusCode is >= 400 and < 500
        && ex.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.RequestTimeout or (HttpStatusCode)429);

    /// <summary>При запуску даємо відхиленим раніше змінам ще одну спробу.</summary>
    private async Task ResetParkedAsync()
    {
        var db = await _localDb.GetConnectionAsync();
        var parked = await db.Table<OutboxItem>().Where(i => i.Attempts >= OutboxStore.MaxAttempts).ToListAsync();
        foreach (var item in parked)
        {
            item.Attempts = 0;
            await db.UpdateAsync(item);
        }
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        if (e.NetworkAccess == NetworkAccess.Internet)
        {
            RequestSync();
        }
        else
        {
            SetStatus(SyncStatus.Offline);
        }
    }

    private void ScheduleRetry()
    {
        Schedule(TimeSpan.FromSeconds(_retrySeconds));
        _retrySeconds = Math.Min(_retrySeconds * 2, MaxRetrySeconds);
    }

    private void Schedule(TimeSpan delay)
    {
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _scheduled, cts);
        previous?.Cancel();

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, cts.Token);
                await SyncNowAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // замінено новішим запитом
            }
        });
    }

    private void CancelScheduled() => Interlocked.Exchange(ref _scheduled, null)?.Cancel();

    private void SetStatus(SyncStatus status)
    {
        if (Status == status)
        {
            return;
        }

        Status = status;
        StatusChanged?.Invoke(status);
    }
}