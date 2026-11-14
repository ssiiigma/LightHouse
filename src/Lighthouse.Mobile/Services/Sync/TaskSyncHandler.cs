using System.Globalization;
using System.Text.Json;
using Lighthouse.Mobile.Data;
using Lighthouse.Mobile.Infrastructure;
using Lighthouse.Mobile.Services.Api;
using Lighthouse.Shared.Contracts.Tasks;
using Lighthouse.Shared.Sync;
using SQLite;

namespace Lighthouse.Mobile.Services.Sync;

public sealed class TaskSyncHandler(IApiClient api, ILocalDB localDb) : IEntitySyncHandler
{
    /// <summary>Запас перед курсором: надійніше за точне порівняння; повторне застосування записів безпечне.</summary>
    private const int CursorOverlapSeconds = 5;

    public string EntityType => EntityTypes.Task;

    public int PullOrder => 20;

    public async Task PushAsync(OutboxItem item, CancellationToken ct)
    {
        var db = await localDb.GetConnectionAsync();

        if (item.Operation == OutboxStore.Delete)
        {
            await api.DeleteAsync($"api/tasks/{item.EntityId}", ct);
            await db.RunInTransactionAsync(tx =>
            {
                var row = tx.Find<LocalTask>(item.EntityId);
                if (row is { IsDeleted: true } && !OutboxStore.HasOtherPending(tx, item))
                {
                    RemoveTask(tx, item.EntityId);
                }
            });
            return;
        }

        var request = JsonSerializer.Deserialize<UpsertTaskRequest>(item.PayloadJson!, AppJson.Options)!;
        var dto = await api.PutAsync<UpsertTaskRequest, TaskDto>($"api/tasks/{item.EntityId}", request, ct);
        await db.RunInTransactionAsync(tx => Acknowledge(tx, item, dto));
    }

    public async Task<int> PullAsync(CancellationToken ct)
    {
        var cursor = SyncCursor.Load(EntityType);
        var path = cursor is { } since
            ? "api/tasks?modifiedSince=" + Uri.EscapeDataString(
                since.AddSeconds(-CursorOverlapSeconds).ToString("O", CultureInfo.InvariantCulture))
            : "api/tasks";

        var items = await api.GetAsync<List<TaskDto>>(path, ct);
        if (items.Count == 0)
        {
            return 0;
        }

        var db = await localDb.GetConnectionAsync();
        var applied = 0;
        await db.RunInTransactionAsync(tx =>
        {
            foreach (var dto in items)
            {
                if (ApplyRemote(tx, dto, force: false))
                {
                    applied++;
                }
            }
        });

        SyncCursor.Save(EntityType, items.Max(i => i.ServerModifiedAtUtc));
        return applied;
    }

    public async Task MarkFailedAsync(OutboxItem item)
    {
        var db = await localDb.GetConnectionAsync();
        await db.RunInTransactionAsync(tx =>
        {
            if (OutboxStore.HasOtherPending(tx, item))
            {
                return; // є новіша зміна — вона ще піде на сервер
            }

            if (tx.Find<LocalTask>(item.EntityId) is { } row)
            {
                row.SyncState = SyncState.Failed;
                tx.Update(row);
            }
        });
    }

    /// <summary>Відповідь сервера на PUT: запис синхронізовано, якщо на пристрої немає новіших незбережених змін.</summary>
    private static void Acknowledge(SQLiteConnection tx, OutboxItem item, TaskDto dto)
    {
        var row = tx.Find<LocalTask>(dto.Id);
        if (row is null)
        {
            return;
        }

        if (OutboxStore.HasOtherPending(tx, item))
        {
            return; // користувач змінив запис, поки йшов запит: нова версія ще буде надіслана
        }

        if (dto.UpdatedAtUtc > Utc.Ensure(row.UpdatedAtUtc))
        {
            ApplyRemote(tx, dto, force: true); // на сервері була новіша правка («останній запис»)
            return;
        }

        row.SyncState = SyncState.Synced;
        row.ServerModifiedAtUtc = dto.ServerModifiedAtUtc;
        tx.Update(row);
    }

    /// <summary>Застосовує запис сервера до локальної бази за правилом <see cref="SyncRules"/>.</summary>
    private static bool ApplyRemote(SQLiteConnection tx, TaskDto dto, bool force)
    {
        var local = tx.Find<LocalTask>(dto.Id);

        if (!force && local is not null)
        {
            if (local.SyncState != SyncState.Synced
                && !SyncRules.ShouldApplyRemote(Utc.Ensure(local.UpdatedAtUtc), dto.UpdatedAtUtc))
            {
                return false; // локальна правка новіша — зберігаємо її
            }

            if (local.SyncState == SyncState.Synced && !dto.IsDeleted
                && Utc.Ensure(local.ServerModifiedAtUtc) >= dto.ServerModifiedAtUtc)
            {
                return false; // вже маємо цю версію (запас перед курсором)
            }
        }

        // Версія сервера перемагає: незавершені локальні зміни цього об'єкта скасовуються.
        tx.Table<OutboxItem>().Delete(i => i.EntityType == EntityTypes.Task && i.EntityId == dto.Id);

        if (dto.IsDeleted)
        {
            RemoveTask(tx, dto.Id);
            return local is not null;
        }

        var row = local ?? new LocalTask { Id = dto.Id };
        row.Title = dto.Title;
        row.Description = dto.Description;
        row.DeadlineUtc = dto.DeadlineUtc;
        row.Priority = dto.Priority;
        row.CategoryId = dto.CategoryId;
        row.ReminderAtUtc = dto.ReminderAtUtc;
        row.CompletedAtUtc = dto.CompletedAtUtc;
        row.EstimatedMinutes = dto.EstimatedMinutes;
        row.UpdatedAtUtc = dto.UpdatedAtUtc;
        row.ServerModifiedAtUtc = dto.ServerModifiedAtUtc;
        row.IsDeleted = false;
        row.SyncState = SyncState.Synced;
        tx.InsertOrReplace(row);

        tx.Table<LocalSubtask>().Delete(s => s.TaskId == dto.Id);
        for (var order = 0; order < dto.Subtasks.Count; order++)
        {
            var subtask = dto.Subtasks[order];
            tx.Insert(new LocalSubtask
            {
                Id = subtask.Id,
                TaskId = dto.Id,
                Title = subtask.Title,
                IsDone = subtask.IsDone,
                SortOrder = order
            });
        }

        return true;
    }

    private static void RemoveTask(SQLiteConnection tx, Guid id)
    {
        tx.Table<LocalSubtask>().Delete(s => s.TaskId == id);
        tx.Delete<LocalTask>(id);
    }
}