using System.Globalization;
using System.Text.Json;
using Lighthouse.Mobile.Data;
using Lighthouse.Mobile.Infrastructure;
using Lighthouse.Mobile.Services.Api;
using Lighthouse.Shared.Contracts.Habits;
using Lighthouse.Shared.Contracts.Tasks;
using Lighthouse.Shared.Sync;
using SQLite;

namespace Lighthouse.Mobile.Services.Sync;

public sealed class HabitSyncHandler(IApiClient api, ILocalDB localDb) : IEntitySyncHandler
{
    private const int CursorOverlapSeconds = 5;

    public string EntityType => EntityTypes.Habit;

    /// <summary>Після категорій (10), до завдань (20) і відміток (30).</summary>
    public int PullOrder => 15;

    public async Task PushAsync(OutboxItem item, CancellationToken ct)
    {
        var db = await localDb.GetConnectionAsync();

        if (item.Operation == OutboxStore.Delete)
        {
            await api.DeleteAsync($"api/habits/{item.EntityId}", ct);
            await db.RunInTransactionAsync(tx =>
            {
                var row = tx.Find<LocalHabit>(item.EntityId);
                if (row is { IsDeleted: true } && !OutboxStore.HasOtherPending(tx, item))
                {
                    RemoveHabit(tx, item.EntityId);
                }
            });
            return;
        }

        var request = JsonSerializer.Deserialize<UpsertHabitRequest>(item.PayloadJson!, AppJson.Options)!;
        var dto = await api.PutAsync<UpsertHabitRequest, HabitDto>($"api/habits/{item.EntityId}", request, ct);
        await db.RunInTransactionAsync(tx =>
        {
            var row = tx.Find<LocalHabit>(dto.Id);
            if (row is null || OutboxStore.HasOtherPending(tx, item))
            {
                return;
            }

            if (dto.UpdatedAtUtc > Utc.Ensure(row.UpdatedAtUtc))
            {
                ApplyRemote(tx, dto, force: true); // на сервері була новіша правка
                return;
            }

            row.SyncState = SyncState.Synced;
            row.ServerModifiedAtUtc = dto.ServerModifiedAtUtc;
            tx.Update(row);
        });
    }

    public async Task<int> PullAsync(CancellationToken ct)
    {
        var db = await localDb.GetConnectionAsync();
        var cursor = await SyncCursor.LoadAsync(db, EntityType);
        var path = cursor is { } since
            ? "api/habits?modifiedSince=" + Uri.EscapeDataString(
                since.AddSeconds(-CursorOverlapSeconds).ToString("O", CultureInfo.InvariantCulture))
            : "api/habits";

        var items = await api.GetAsync<List<HabitDto>>(path, ct);
        return await PullHelper.ApplyAsync(db, EntityType, items,
            (tx, dto) => ApplyRemote(tx, dto, force: false), d => d.ServerModifiedAtUtc);
    }

    public async Task MarkFailedAsync(OutboxItem item)
    {
        var db = await localDb.GetConnectionAsync();
        await db.RunInTransactionAsync(tx =>
        {
            if (OutboxStore.HasOtherPending(tx, item))
            {
                return;
            }

            if (tx.Find<LocalHabit>(item.EntityId) is { } row)
            {
                row.SyncState = SyncState.Failed;
                tx.Update(row);
            }
        });
    }

    private static bool ApplyRemote(SQLiteConnection tx, HabitDto dto, bool force)
    {
        var local = tx.Find<LocalHabit>(dto.Id);
        var applyFields = true;

        if (!force && local is not null)
        {
            if (local.SyncState != SyncState.Synced
                && !SyncRules.ShouldApplyRemote(Utc.Ensure(local.UpdatedAtUtc), dto.UpdatedAtUtc))
            {
                if (dto.IsDeleted)
                {
                    return false; // локальна правка новіша за видалення
                }

                applyFields = false; // поля лишаємо локальні, але відмітки з сервера зливаємо
            }
            else if (local.SyncState == SyncState.Synced && !dto.IsDeleted
                     && Utc.Ensure(local.ServerModifiedAtUtc) >= dto.ServerModifiedAtUtc)
            {
                return false; // вже маємо цю версію (запас перед курсором)
            }
        }

        if (dto.IsDeleted)
        {
            tx.Table<OutboxItem>().Delete(i => i.EntityType == EntityTypes.Habit && i.EntityId == dto.Id);
            RemoveHabit(tx, dto.Id);
            return local is not null;
        }

        if (applyFields)
        {
            tx.Table<OutboxItem>().Delete(i => i.EntityType == EntityTypes.Habit && i.EntityId == dto.Id);

            var row = local ?? new LocalHabit { Id = dto.Id };
            row.Title = dto.Title;
            row.Description = dto.Description;
            row.FrequencyType = dto.FrequencyType;
            row.DaysOfWeekMask = dto.DaysOfWeekMask;
            row.TimesPerWeek = dto.TimesPerWeek;
            row.ReminderMinutes = dto.ReminderTime is { } time ? time.Hour * 60 + time.Minute : null;
            row.ColorHex = dto.ColorHex;
            row.Icon = dto.Icon;
            row.UpdatedAtUtc = dto.UpdatedAtUtc;
            row.ServerModifiedAtUtc = dto.ServerModifiedAtUtc;
            row.IsDeleted = false;
            row.SyncState = SyncState.Synced;
            tx.InsertOrReplace(row);
        }

        MergeLogs(tx, dto);
        return true;
    }

    /// <summary>Відмітки сервера зливаються з локальними: ще не надіслані локальні зміни не втрачаються.</summary>
    private static void MergeLogs(SQLiteConnection tx, HabitDto dto)
    {
        var serverDates = dto.Logs.Select(l => HabitKeys.Format(l.Date)).ToHashSet();

        foreach (var log in tx.Table<LocalHabitLog>().Where(l => l.HabitId == dto.Id).ToList())
        {
            if (log.SyncState == SyncState.Synced && !serverDates.Contains(log.Date))
            {
                tx.Delete(log); // знято на іншому пристрої
            }
        }

        foreach (var serverLog in dto.Logs)
        {
            var date = HabitKeys.Format(serverLog.Date);
            var id = HabitKeys.LogId(dto.Id, date);
            if (tx.Find<LocalHabitLog>(id) is not null)
            {
                continue;
            }

            // Якщо відмітку щойно зняли локально й видалення ще в черзі, не відновлюємо її.
            var pendingDelete = tx.Table<OutboxItem>().Count(i =>
                i.EntityType == EntityTypes.HabitLog && i.EntityId == id && i.Operation == OutboxStore.Delete) > 0;
            if (pendingDelete)
            {
                continue;
            }

            tx.Insert(new LocalHabitLog
            {
                Id = id,
                HabitId = dto.Id,
                Date = date,
                CompletedAtUtc = serverLog.CompletedAtUtc,
                SyncState = SyncState.Synced
            });
        }
    }

    private static void RemoveHabit(SQLiteConnection tx, Guid id)
    {
        tx.Table<LocalHabitLog>().Delete(l => l.HabitId == id);
        tx.Delete<LocalHabit>(id);

        // Операції з відмітками видаленої звички більше не потрібні.
        foreach (var op in tx.Table<OutboxItem>().Where(i => i.EntityType == EntityTypes.HabitLog).ToList())
        {
            if (HabitKeys.ReadPayload(op.PayloadJson)?.HabitId == id)
            {
                tx.Delete(op);
            }
        }
    }
}