namespace Lighthouse.Mobile.Services.Sync;

public enum SyncStatus
{
    Idle,
    Syncing,
    Offline,
    Error
}

/// <summary>
/// Фонова синхронізація: надсилає чергу Outbox і тягне дельту за курсором ServerModifiedAtUtc.
/// Запускається при зміні даних, на старті, при відновленні мережі та за pull-to-refresh; при помилці повторює.
/// </summary>
public interface ISyncSvc
{
    SyncStatus Status { get; }
    event Action<SyncStatus>? StatusChanged;

    /// <summary>Локальні дані змінились унаслідок синхронізації (потрібно перечитати списки).</summary>
    event Action? DataChanged;

    /// <summary>Підписується на зміну мережі й запускає синхронізацію; повторні виклики лише запитують її.</summary>
    void Start();

    void Stop();

    /// <summary>Запит синхронізації з затримкою 1 с (зливає серію швидких змін в одну).</summary>
    void RequestSync();

    /// <summary>Синхронізація зараз (pull-to-refresh); false — без мережі або з помилкою.</summary>
    Task<bool> SyncNowAsync(CancellationToken ct = default);

    /// <summary>Кількість ще не надісланих змін.</summary>
    Task<int> GetPendingCountAsync();
}