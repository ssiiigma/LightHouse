using System.Net;
using System.Text.Json;
using Lighthouse.Mobile.Data;
using Lighthouse.Mobile.Infrastructure;
using Lighthouse.Mobile.Services.Api;
using Microsoft.Extensions.Logging;

namespace Lighthouse.Mobile.Services.Sync;

public sealed class SyncService : ISyncSvc
{
    private const string LastOkKey = "sync.last_ok";
    private const string LastErrorKey = "sync.last_error";
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

    public async Task<ISyncSvc.SyncDiagnostics> GetDiagnosticsAsync()
    {
        var db = await _localDb.GetConnectionAsync();
        var pending = await db.Table<OutboxItem>().Where(i => i.Attempts < OutboxStore.MaxAttempts).CountAsync();
        var parked = await db.Table<OutboxItem>().Where(i => i.Attempts >= OutboxStore.MaxAttempts).CountAsync();

        DateTime? lastOk = long.TryParse(Preferences.Default.Get(LastOkKey, string.Empty), out var ticks)
            ? new DateTime(ticks, DateTimeKind.Utc)
            : null;
        var error = Preferences.Default.Get(LastErrorKey, string.Empty);

        return new ISyncSvc.SyncDiagnostics(pending, parked, lastOk, error.Length == 0 ? null : error);
    }

    public async Task RetryParkedAsync()
    {
        await ResetParkedAsync();
        await SyncNowAsync();
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

            // Кожен тип завантажується окремо: збій одного (наприклад, звичок) не блокує решту (завдання).
            var errors = new List<string>();
            foreach (var handler in _handlers)
            {
                try
                {
                    changed += await handler.PullAsync(ct);
                }
                catch (SyncPullException ex)
                {
                    changed += ex.Applied;
                    errors.Add(ex.Message);
                    _logger.LogWarning("Pull of {Type} partially failed: {Message}", handler.EntityType, ex.Message);
                }
                catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
                {
                    throw;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    errors.Add($"{handler.EntityType}: {Trim(ex.Message)}");
                    _logger.LogWarning(ex, "Pull of {Type} failed.", handler.EntityType);
                }
            }

            if (changed > 0)
            {
                DataChanged?.Invoke();
            }

            if (errors.Count > 0)
            {
                RememberError(string.Join(" | ", errors));
                SetStatus(SyncStatus.Error);
                ScheduleRetry();
                return false;
            }

            _retrySeconds = InitialRetrySeconds;
            Preferences.Default.Set(LastOkKey, DateTime.UtcNow.Ticks.ToString());
            Preferences.Default.Remove(LastErrorKey);
            SetStatus(SyncStatus.Idle);
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
            RememberError("Session expired");
            SetStatus(SyncStatus.Error);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Synchronization failed; will retry in {Seconds} s.", _retrySeconds);
            RememberError(ex.Message);
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

            // Порядок: категорії, звички, завдання, відмітки, відгуки; всередині типу — за часом створення.
            var queue = await db.Table<OutboxItem>().Where(i => i.Attempts < OutboxStore.MaxAttempts).ToListAsync();
            var item = queue
                .OrderBy(OrderOf)
                .ThenBy(i => i.Id)
                .FirstOrDefault();
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
                // Сервер відхилив саме цю зміну (400, 404, 409...): не блокуємо чергу, зміна лишається на пристрої.
                _logger.LogWarning("Server rejected {Type} {Id}: {Status}.", item.EntityType, item.EntityId, ex.StatusCode);
                await ParkAsync(db, handler, item, $"{(int)ex.StatusCode}: {Trim(ex.Message)}");
            }
            catch (ApiException ex) when ((int)ex.StatusCode >= 500)
            {
                item.Attempts++;
                item.LastError = Trim(ex.Message);
                await db.UpdateAsync(item);
                throw;
            }
            catch (Exception ex) when (IsPoison(ex))
            {
                // «Отруйний» елемент (пошкоджені дані черги, помилка обробки): його не вдасться надіслати ніколи,
                // тому він відкладається, а решта черги продовжує надсилатись.
                _logger.LogError(ex, "Outbox item {Type} {Id} cannot be processed.", item.EntityType, item.EntityId);
                await ParkAsync(db, handler, item, $"{ex.GetType().Name}: {Trim(ex.Message)}");
            }
        }
    }

    private static async Task ParkAsync(
        SQLite.SQLiteAsyncConnection db, IEntitySyncHandler handler, OutboxItem item, string error)
    {
        item.Attempts = OutboxStore.MaxAttempts;
        item.LastError = error;
        await db.UpdateAsync(item);
        await handler.MarkFailedAsync(item);
    }

    /// <summary>Помилки 4xx, повторення яких нічого не змінить (крім 401 — потрібен вхід, 408 і 429 — тимчасові).</summary>
    private static bool IsPermanent(ApiException ex) =>
        (int)ex.StatusCode is >= 400 and < 500
        && ex.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.RequestTimeout or (HttpStatusCode)429);

    private static bool IsPoison(Exception ex) =>
        ex is JsonException or InvalidOperationException or ArgumentException or FormatException
            or NullReferenceException or KeyNotFoundException;

    /// <summary>При запуску й за командою користувача відкладеним змінам даємо ще одну спробу.</summary>
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

    private static void RememberError(string message) => Preferences.Default.Set(LastErrorKey, Trim(message));

    private static string Trim(string text) => text.Length <= 160 ? text : text[..160];

    private void SetStatus(SyncStatus status)
    {
        if (Status == status)
        {
            return;
        }

        Status = status;
        StatusChanged?.Invoke(status);
    }
    
    public async Task<bool> FullResyncAsync()
    {
        var db = await _localDb.GetConnectionAsync();
        await SyncCursor.ResetAsync(db);
        return await SyncNowAsync();
    }
    
    /// <summary>Видалення категорії надсилається після завдань, які на неї посилались (інакше сервер сам скине їм категорію новішим часом).</summary>
    private int OrderOf(OutboxItem item) =>
        item.EntityType == EntityTypes.Category && item.Operation == OutboxStore.Delete
            ? 50
            : _handlers.FirstOrDefault(h => h.EntityType == item.EntityType)?.PullOrder ?? int.MaxValue;
}