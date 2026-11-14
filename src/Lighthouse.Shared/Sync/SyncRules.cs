namespace Lighthouse.Shared.Sync;

/// <summary>
/// Правило вирішення конфліктів синхронізації «останній запис за UpdatedAtUtc».
/// Спільне для клієнта й сервера: сервер ігнорує запис клієнта, якщо його власна правка новіша
/// (<c>task.UpdatedAtUtc &gt; editedAt</c>), тож за рівних міток перемагає клієнт.
/// </summary>
public static class SyncRules
{
    /// <summary>
    /// Чи застосовувати отримані від сервера дані до локального запису.
    /// </summary>
    /// <param name="localPendingUpdatedAtUtc">Час редагування локального запису з ще не надісланими змінами; null — незавершених змін немає.</param>
    /// <param name="remoteUpdatedAtUtc">Час редагування запису на сервері.</param>
    public static bool ShouldApplyRemote(DateTime? localPendingUpdatedAtUtc, DateTime remoteUpdatedAtUtc) =>
        localPendingUpdatedAtUtc is null || remoteUpdatedAtUtc > localPendingUpdatedAtUtc.Value;
}