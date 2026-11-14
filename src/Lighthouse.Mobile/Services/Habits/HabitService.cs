using System.Text.Json;
using Lighthouse.Mobile.Data;
using Lighthouse.Mobile.Infrastructure;
using Lighthouse.Mobile.Models;
using Lighthouse.Mobile.Services.Sync;
using Lighthouse.Shared.Contracts.Habits;

namespace Lighthouse.Mobile.Services.Habits;

/// <summary>Звички й відмітки: зміни пишуться в SQLite й ставляться в чергу Outbox; серії рахуються на пристрої.</summary>
public interface IHabitSvc
{
    event Action? Changed;

    Task<IReadOnlyList<HabitModel>> GetHabitsAsync();
    Task<HabitModel?> GetAsync(Guid id);
    Task<Guid> SaveAsync(HabitModel model);
    Task DeleteAsync(Guid id);

    /// <summary>Ставить або знімає відмітку за день (ідемпотентно для повторної синхронізації).</summary>
    Task ToggleAsync(Guid habitId, DateOnly date);
}

public sealed class HabitService(ILocalDB localDb, ISyncSvc sync) : IHabitSvc
{
    public event Action? Changed;

    public async Task<IReadOnlyList<HabitModel>> GetHabitsAsync()
    {
        var db = await localDb.GetConnectionAsync();
        var rows = await db.Table<LocalHabit>().Where(h => !h.IsDeleted).ToListAsync();
        var logs = await db.Table<LocalHabitLog>().ToListAsync();
        var byHabit = logs.GroupBy(l => l.HabitId).ToDictionary(g => g.Key, g => g.Select(l => HabitKeys.Parse(l.Date)).ToList());

        return rows
            .OrderBy(h => h.Title)
            .Select(h => ToModel(h, byHabit.GetValueOrDefault(h.Id) ?? []))
            .ToList();
    }

    public async Task<HabitModel?> GetAsync(Guid id)
    {
        var db = await localDb.GetConnectionAsync();
        var row = await db.FindAsync<LocalHabit>(id);
        if (row is null || row.IsDeleted)
        {
            return null;
        }

        var logs = await db.Table<LocalHabitLog>().Where(l => l.HabitId == id).ToListAsync();
        return ToModel(row, logs.Select(l => HabitKeys.Parse(l.Date)).ToList());
    }

    public async Task<Guid> SaveAsync(HabitModel model)
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
            var row = tx.Find<LocalHabit>(id) ?? new LocalHabit { Id = id };
            row.Title = title;
            row.Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim();
            row.FrequencyType = model.FrequencyType;
            row.DaysOfWeekMask = model.FrequencyType == Lighthouse.Shared.Enums.HabitFrequencyType.SpecificDays ? model.DaysOfWeekMask : 0;
            row.TimesPerWeek = model.FrequencyType == Lighthouse.Shared.Enums.HabitFrequencyType.TimesPerWeek ? model.TimesPerWeek : null;
            row.ReminderMinutes = model.ReminderMinutes;
            row.ColorHex = model.ColorHex;
            row.Icon = model.Icon;
            row.IsDeleted = false;
            row.UpdatedAtUtc = now;
            row.SyncState = SyncState.Pending;
            tx.InsertOrReplace(row);

            OutboxStore.Enqueue(tx, EntityTypes.Habit, id, OutboxStore.Put, Serialize(row));
        });

        Notify();
        return id;
    }

    public async Task DeleteAsync(Guid id)
    {
        var now = Utc.Now;
        var db = await localDb.GetConnectionAsync();

        await db.RunInTransactionAsync(tx =>
        {
            if (tx.Find<LocalHabit>(id) is not { } row)
            {
                return;
            }

            row.IsDeleted = true;
            row.UpdatedAtUtc = now;
            row.SyncState = SyncState.Pending;
            tx.Update(row);
            OutboxStore.Enqueue(tx, EntityTypes.Habit, id, OutboxStore.Delete, null);
        });

        Notify();
    }

    public async Task ToggleAsync(Guid habitId, DateOnly date)
    {
        var dateText = HabitKeys.Format(date);
        var logId = HabitKeys.LogId(habitId, dateText);
        var db = await localDb.GetConnectionAsync();

        await db.RunInTransactionAsync(tx =>
        {
            if (tx.Find<LocalHabit>(habitId) is not { IsDeleted: false })
            {
                return;
            }

            var existing = tx.Find<LocalHabitLog>(logId);
            if (existing is null)
            {
                var completedAt = Utc.Now;
                tx.Insert(new LocalHabitLog
                {
                    Id = logId,
                    HabitId = habitId,
                    Date = dateText,
                    CompletedAtUtc = completedAt,
                    SyncState = SyncState.Pending
                });
                OutboxStore.Enqueue(tx, EntityTypes.HabitLog, logId, OutboxStore.Put,
                    JsonSerializer.Serialize(new HabitLogPayload(habitId, dateText, completedAt), AppJson.Options));
            }
            else
            {
                tx.Delete<LocalHabitLog>(logId);
                OutboxStore.Enqueue(tx, EntityTypes.HabitLog, logId, OutboxStore.Delete,
                    JsonSerializer.Serialize(new HabitLogPayload(habitId, dateText, null), AppJson.Options));
            }
        });

        Notify();
    }

    private void Notify()
    {
        Changed?.Invoke();
        sync.RequestSync();
    }

    private static string Serialize(LocalHabit row) =>
        JsonSerializer.Serialize(new UpsertHabitRequest(
            row.Title,
            row.Description,
            row.FrequencyType,
            row.DaysOfWeekMask,
            row.TimesPerWeek,
            row.ReminderMinutes is { } minutes ? TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(minutes)) : null,
            row.ColorHex,
            row.Icon,
            null,
            Utc.Ensure(row.UpdatedAtUtc)), AppJson.Options);

    private static HabitModel ToModel(LocalHabit row, List<DateOnly> logs) => new()
    {
        Id = row.Id,
        Title = row.Title,
        Description = row.Description,
        FrequencyType = row.FrequencyType,
        DaysOfWeekMask = row.DaysOfWeekMask,
        TimesPerWeek = row.TimesPerWeek,
        ReminderMinutes = row.ReminderMinutes,
        ColorHex = row.ColorHex,
        Icon = row.Icon,
        SyncState = row.SyncState,
        LogDates = logs
    };
}