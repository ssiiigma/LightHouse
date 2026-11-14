using System.Collections.Concurrent;
using System.Text.Json;
using Lighthouse.Mobile.Data;
using Lighthouse.Mobile.Infrastructure;
using Lighthouse.Mobile.Models;
using Lighthouse.Mobile.Services.Sync;
using Lighthouse.Shared.Contracts.Tasks;
using Lighthouse.Shared.Enums;
using Lighthouse.Shared.Contracts.Categories;

namespace Lighthouse.Mobile.Services.Tasks;

/// <summary>
/// Завдання: усі зміни спершу пишуться в SQLite (інтерфейс оновлюється одразу), а в чергу Outbox
/// потрапляє операція для фонової синхронізації.
/// </summary>
public interface ITaskSvc
{
    /// <summary>Локальні дані змінились.</summary>
    event Action? Changed;

    /// <summary>Усі завдання, крім видалених (виконані також).</summary>
    Task<IReadOnlyList<TaskModel>> GetTasksAsync();

    Task<TaskModel?> GetAsync(Guid id);

    /// <summary>Створення або оновлення; для нового завдання ідентифікатор генерується тут.</summary>
    Task<Guid> SaveAsync(TaskModel model);

    Task SetCompletedAsync(Guid id, bool completed);

    /// <summary>М'яке видалення: 5 с можна скасувати (<see cref="UndoDeleteAsync"/>), потім видалення йде на сервер.</summary>
    Task DeleteAsync(Guid id);

    Task UndoDeleteAsync(Guid id);

    /// <summary>Після запуску: підтверджує видалення, яке не встигли поставити в чергу (застосунок закрили в межах 5 с).</summary>
    Task RecoverAsync();

    Task<IReadOnlyList<CategoryModel>> GetCategoriesAsync();
    
    Task<Guid> SaveCategoryAsync(string name);
}

public sealed class TaskService(ILocalDB localDb, ISyncSvc sync) : ITaskSvc
{
    private static readonly TimeSpan UndoWindow = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _pendingDeletes = new();

    public event Action? Changed;
    
    private static readonly string[] CategoryPalette = ["#4C8BF5", "#5BB97C", "#E8A23A", "#805AD5", "#E05C7B", "#38B2AC"];

    public async Task<Guid> SaveCategoryAsync(string name)
    {
        var title = name.Trim();
        if (title.Length == 0)
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }

        var id = Guid.NewGuid();
        var now = Utc.Now;
        var db = await localDb.GetConnectionAsync();

        await db.RunInTransactionAsync(tx =>
        {
            var count = tx.Table<LocalCategory>().Count(c => !c.IsDeleted);
            var row = new LocalCategory
            {
                Id = id,
                Name = title,
                ColorHex = CategoryPalette[count % CategoryPalette.Length],
                UpdatedAtUtc = now,
                SyncState = SyncState.Pending
            };
            tx.Insert(row);
            OutboxStore.Enqueue(tx, EntityTypes.Category, id, OutboxStore.Put,
                JsonSerializer.Serialize(new UpsertCategoryRequest(row.Name, row.ColorHex, row.Icon, now), AppJson.Options));
        });

        Notify();
        return id;
    }

    public async Task<IReadOnlyList<TaskModel>> GetTasksAsync()
    {
        var db = await localDb.GetConnectionAsync();
        var rows = await db.Table<LocalTask>().Where(t => !t.IsDeleted).ToListAsync();
        var subtasks = await db.Table<LocalSubtask>().ToListAsync();
        var byTask = subtasks.GroupBy(s => s.TaskId).ToDictionary(g => g.Key, g => g.OrderBy(s => s.SortOrder).ToList());

        return rows.Select(r => ToModel(r, byTask.GetValueOrDefault(r.Id) ?? new List<LocalSubtask>())).ToList();
    }

    public async Task<TaskModel?> GetAsync(Guid id)
    {
        var db = await localDb.GetConnectionAsync();
        var row = await db.FindAsync<LocalTask>(id);
        if (row is null || row.IsDeleted)
        {
            return null;
        }

        var subtasks = await db.Table<LocalSubtask>().Where(s => s.TaskId == id).OrderBy(s => s.SortOrder).ToListAsync();
        return ToModel(row, subtasks);
    }

    public async Task<Guid> SaveAsync(TaskModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var title = model.Title.Trim();
        if (title.Length == 0)
        {
            throw new ArgumentException("Title is required.", nameof(model));
        }

        var id = model.Id == Guid.Empty ? Guid.NewGuid() : model.Id;
        var now = Utc.Now;
        var db = await localDb.GetConnectionAsync();

        await db.RunInTransactionAsync(tx =>
        {
            var row = tx.Find<LocalTask>(id) ?? new LocalTask { Id = id };
            row.Title = title;
            row.Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim();
            row.DeadlineUtc = Utc.Ensure(model.DeadlineUtc);
            row.Priority = model.Priority;
            row.CategoryId = model.CategoryId;
            row.ReminderAtUtc = Utc.Ensure(model.ReminderAtUtc);
            row.CompletedAtUtc = Utc.Ensure(model.CompletedAtUtc);
            row.EstimatedMinutes = model.EstimatedMinutes;
            row.IsDeleted = false;
            row.UpdatedAtUtc = now;
            row.SyncState = SyncState.Pending;
            tx.InsertOrReplace(row);

            tx.Table<LocalSubtask>().Delete(s => s.TaskId == id);
            var order = 0;
            var subtasks = new List<LocalSubtask>();
            foreach (var subtask in model.Subtasks.Where(s => !string.IsNullOrWhiteSpace(s.Title)))
            {
                var local = new LocalSubtask
                {
                    Id = subtask.Id == Guid.Empty ? Guid.NewGuid() : subtask.Id,
                    TaskId = id,
                    Title = subtask.Title.Trim(),
                    IsDone = subtask.IsDone,
                    SortOrder = order++
                };
                tx.Insert(local);
                subtasks.Add(local);
            }

            OutboxStore.Enqueue(tx, EntityTypes.Task, id, OutboxStore.Put, Serialize(row, subtasks));
        });

        Notify();
        return id;
    }

    public async Task SetCompletedAsync(Guid id, bool completed)
    {
        var now = Utc.Now;
        var db = await localDb.GetConnectionAsync();

        await db.RunInTransactionAsync(tx =>
        {
            var row = tx.Find<LocalTask>(id);
            if (row is null || row.IsDeleted)
            {
                return;
            }

            row.CompletedAtUtc = completed ? now : null;
            row.UpdatedAtUtc = now;
            row.SyncState = SyncState.Pending;
            tx.Update(row);

            var subtasks = tx.Table<LocalSubtask>().Where(s => s.TaskId == id).OrderBy(s => s.SortOrder).ToList();
            OutboxStore.Enqueue(tx, EntityTypes.Task, id, OutboxStore.Put, Serialize(row, subtasks));
        });

        Notify();
    }

    public async Task DeleteAsync(Guid id)
    {
        var now = Utc.Now;
        var db = await localDb.GetConnectionAsync();

        await db.RunInTransactionAsync(tx =>
        {
            if (tx.Find<LocalTask>(id) is not { } row)
            {
                return;
            }

            row.IsDeleted = true; // лише ховаємо; у чергу видалення потрапить за 5 с
            row.UpdatedAtUtc = now;
            row.SyncState = SyncState.Pending;
            tx.Update(row);
        });

        var cts = new CancellationTokenSource();
        if (_pendingDeletes.TryRemove(id, out var previous))
        {
            previous.Cancel();
        }

        _pendingDeletes[id] = cts;
        Changed?.Invoke();
        _ = CommitDeleteAfterWindowAsync(id, cts);
    }

    public async Task UndoDeleteAsync(Guid id)
    {
        if (!_pendingDeletes.TryRemove(id, out var cts))
        {
            return; // вікно скасування минуло
        }

        cts.Cancel();

        var now = Utc.Now;
        var db = await localDb.GetConnectionAsync();
        await db.RunInTransactionAsync(tx =>
        {
            if (tx.Find<LocalTask>(id) is not { IsDeleted: true } row)
            {
                return;
            }

            row.IsDeleted = false;
            row.UpdatedAtUtc = now;
            row.SyncState = SyncState.Pending;
            tx.Update(row);

            var subtasks = tx.Table<LocalSubtask>().Where(s => s.TaskId == id).OrderBy(s => s.SortOrder).ToList();
            OutboxStore.Enqueue(tx, EntityTypes.Task, id, OutboxStore.Put, Serialize(row, subtasks));
        });

        Notify();
    }

    public async Task RecoverAsync()
    {
        var db = await localDb.GetConnectionAsync();
        await db.RunInTransactionAsync(tx =>
        {
            foreach (var row in tx.Table<LocalTask>().Where(t => t.IsDeleted).ToList())
            {
                if (_pendingDeletes.ContainsKey(row.Id))
                {
                    continue;
                }

                var id = row.Id;
                var hasDelete = tx.Table<OutboxItem>().Count(i =>
                    i.EntityType == EntityTypes.Task && i.EntityId == id && i.Operation == OutboxStore.Delete) > 0;
                if (!hasDelete)
                {
                    OutboxStore.Enqueue(tx, EntityTypes.Task, id, OutboxStore.Delete, null);
                }
            }
        });
    }

    public async Task<IReadOnlyList<CategoryModel>> GetCategoriesAsync()
    {
        var db = await localDb.GetConnectionAsync();
        var rows = await db.Table<LocalCategory>().Where(c => !c.IsDeleted).ToListAsync();
        return rows
            .OrderBy(c => c.Name)
            .Select(c => new CategoryModel { Id = c.Id, Name = c.Name, ColorHex = c.ColorHex })
            .ToList();
    }

    private async Task CommitDeleteAfterWindowAsync(Guid id, CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(UndoWindow, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return; // скасовано користувачем або замінено новим видаленням
        }

        _pendingDeletes.TryRemove(new KeyValuePair<Guid, CancellationTokenSource>(id, cts));

        var db = await localDb.GetConnectionAsync();
        await db.RunInTransactionAsync(tx =>
        {
            if (tx.Find<LocalTask>(id) is { IsDeleted: true })
            {
                OutboxStore.Enqueue(tx, EntityTypes.Task, id, OutboxStore.Delete, null);
            }
        });

        sync.RequestSync();
    }

    private void Notify()
    {
        Changed?.Invoke();
        sync.RequestSync();
    }

    private static string Serialize(LocalTask row, IReadOnlyList<LocalSubtask> subtasks) =>
        JsonSerializer.Serialize(new UpsertTaskRequest(
            row.Title,
            row.Description,
            Utc.Ensure(row.DeadlineUtc),
            row.Priority,
            row.CategoryId,
            Utc.Ensure(row.ReminderAtUtc),
            Utc.Ensure(row.CompletedAtUtc),
            row.EstimatedMinutes,
            subtasks.Select(s => new SubtaskDto(s.Id, s.Title, s.IsDone)).ToList(),
            Utc.Ensure(row.UpdatedAtUtc)), AppJson.Options);

    private static TaskModel ToModel(LocalTask row, List<LocalSubtask> subtasks) => new()
    {
        Id = row.Id,
        Title = row.Title,
        Description = row.Description,
        DeadlineUtc = Utc.Ensure(row.DeadlineUtc),
        Priority = row.Priority,
        CategoryId = row.CategoryId,
        ReminderAtUtc = Utc.Ensure(row.ReminderAtUtc),
        CompletedAtUtc = Utc.Ensure(row.CompletedAtUtc),
        EstimatedMinutes = row.EstimatedMinutes,
        Subtasks = subtasks.Select(s => new SubtaskModel { Id = s.Id, Title = s.Title, IsDone = s.IsDone }).ToList(),
        UpdatedAtUtc = Utc.Ensure(row.UpdatedAtUtc),
        SyncState = row.SyncState
    };
}