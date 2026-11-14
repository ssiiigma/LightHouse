using Lighthouse.Mobile.Data;
using Lighthouse.Mobile.Infrastructure;
using Lighthouse.Mobile.Services.Api;
using Lighthouse.Shared.Contracts.Habits;

namespace Lighthouse.Mobile.Services.Sync;

/// <summary>Надсилання відміток: PUT/DELETE /api/habits/{id}/logs/{yyyy-MM-dd}. Отримання відбувається разом зі звичками.</summary>
public sealed class HabitLogSyncHandler(IApiClient api, ILocalDB localDb) : IEntitySyncHandler
{
    public string EntityType => EntityTypes.HabitLog;

    /// <summary>Після звичок: відмітка не може обігнати створення звички.</summary>
    public int PullOrder => 30;

    public async Task PushAsync(OutboxItem item, CancellationToken ct)
    {
        var payload = HabitKeys.ReadPayload(item.PayloadJson)
                      ?? throw new InvalidOperationException("Habit log payload is empty.");
        var path = $"api/habits/{payload.HabitId}/logs/{payload.Date}";

        if (item.Operation == OutboxStore.Delete)
        {
            await api.DeleteAsync(path, ct);
            return;
        }

        await api.PutAsync<UpsertHabitLogRequest, HabitDto>(path, new UpsertHabitLogRequest(payload.CompletedAtUtc), ct);

        var db = await localDb.GetConnectionAsync();
        await db.RunInTransactionAsync(tx =>
        {
            if (tx.Find<LocalHabitLog>(item.EntityId) is { } row && !OutboxStore.HasOtherPending(tx, item))
            {
                row.SyncState = SyncState.Synced;
                tx.Update(row);
            }
        });
    }

    public Task<int> PullAsync(CancellationToken ct) => Task.FromResult(0);

    public async Task MarkFailedAsync(OutboxItem item)
    {
        var db = await localDb.GetConnectionAsync();
        await db.RunInTransactionAsync(tx =>
        {
            if (tx.Find<LocalHabitLog>(item.EntityId) is { } row && !OutboxStore.HasOtherPending(tx, item))
            {
                row.SyncState = SyncState.Failed;
                tx.Update(row);
            }
        });
    }
}