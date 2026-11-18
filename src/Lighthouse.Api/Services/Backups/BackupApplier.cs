using System.Text.RegularExpressions;
using Lighthouse.Api.Data;
using Lighthouse.Api.Infrastructure;
using Lighthouse.Core.Entities;
using Lighthouse.Shared.Categories;
using Lighthouse.Shared.Contracts.Backups;
using Lighthouse.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Lighthouse.Api.Services.Backups;

/// <summary>
/// Застосування документа до даних користувача: <see cref="ReplaceAsync"/> (відновлення із збереженням ідентифікаторів)
/// і <see cref="AddAsync"/> (імпорт: нові ідентифікатори, категорії об'єднуються за назвою).
/// </summary>
public sealed partial class BackupApplier(LighthouseDbContext db)
{
    public async Task<(int Categories, int Tasks, int Habits)> ReplaceAsync(
        Guid userId, BackupDocument source, CancellationToken ct)
    {
        var doc = Normalize(source, out _);
        var now = DateTime.UtcNow;

        await EnsureNoForeignIdsAsync(userId, doc, ct);

        var categories = await db.Categories.Where(c => c.UserId == userId).ToListAsync(ct);
        var tasks = await db.Tasks.Include(t => t.Subtasks).Where(t => t.UserId == userId).ToListAsync(ct);
        var habits = await db.Habits.Include(h => h.Logs).Where(h => h.UserId == userId).ToListAsync(ct);

        // Категорії.
        var categoryById = categories.ToDictionary(c => c.Id);
        var categoryIds = doc.Categories.Select(c => c.Id).ToHashSet();
        foreach (var item in doc.Categories)
        {
            if (!categoryById.TryGetValue(item.Id, out var row))
            {
                row = new Category { Id = item.Id, UserId = userId, CreatedAtUtc = now };
                db.Categories.Add(row);
            }

            row.Name = item.Name;
            row.ColorHex = item.ColorHex;
            row.Icon = item.Icon;
            Stamp(row, now);
        }

        foreach (var stale in categories.Where(c => !categoryIds.Contains(c.Id) && !c.IsDeleted))
        {
            Tombstone(stale, now);
        }

        // Завдання.
        var taskById = tasks.ToDictionary(t => t.Id);
        var taskIds = doc.Tasks.Select(t => t.Id).ToHashSet();
        foreach (var item in doc.Tasks)
        {
            if (!taskById.TryGetValue(item.Id, out var row))
            {
                row = new TaskItem { Id = item.Id, UserId = userId, CreatedAtUtc = now };
                db.Tasks.Add(row);
            }

            FillTask(row, item, item.CategoryId is { } c && categoryIds.Contains(c) ? c : null);
            Stamp(row, now);
            SyncSubtasks(row, item.Subtasks, now);
        }

        foreach (var stale in tasks.Where(t => !taskIds.Contains(t.Id) && !t.IsDeleted))
        {
            Tombstone(stale, now);
        }

        // Звички.
        var habitById = habits.ToDictionary(h => h.Id);
        var habitIds = doc.Habits.Select(h => h.Id).ToHashSet();
        foreach (var item in doc.Habits)
        {
            if (!habitById.TryGetValue(item.Id, out var row))
            {
                row = new Habit { Id = item.Id, UserId = userId, CreatedAtUtc = now };
                db.Habits.Add(row);
            }

            FillHabit(row, item, item.CategoryId is { } c && categoryIds.Contains(c) ? c : null);
            Stamp(row, now);
            SyncLogs(row, item.Logs, now);
        }

        foreach (var stale in habits.Where(h => !habitIds.Contains(h.Id) && !h.IsDeleted))
        {
            Tombstone(stale, now);
        }

        await db.SaveChangesAsync(ct);
        return (doc.Categories.Count, doc.Tasks.Count, doc.Habits.Count);
    }

    public async Task<ImportResultDto> AddAsync(Guid userId, BackupDocument source, CancellationToken ct)
    {
        var doc = Normalize(source, out var skipped);
        var now = DateTime.UtcNow;

        var existing = await db.Categories.Where(c => c.UserId == userId && !c.IsDeleted).ToListAsync(ct);
        var map = new Dictionary<Guid, Guid>();
        var createdCategories = 0;
        foreach (var item in doc.Categories)
        {
            var match = existing.FirstOrDefault(e => CategoryNames.IsSame(e.Name, item.Name));
            if (match is null)
            {
                match = new Category
                {
                    Id = Guid.NewGuid(), UserId = userId, Name = item.Name, ColorHex = item.ColorHex,
                    Icon = item.Icon, CreatedAtUtc = now
                };
                Stamp(match, now);
                db.Categories.Add(match);
                existing.Add(match);
                createdCategories++;
            }

            map[item.Id] = match.Id;
        }

        foreach (var item in doc.Tasks)
        {
            var row = new TaskItem { Id = Guid.NewGuid(), UserId = userId, CreatedAtUtc = now };
            FillTask(row, item, item.CategoryId is { } c && map.TryGetValue(c, out var mapped) ? mapped : null);
            Stamp(row, now);
            db.Tasks.Add(row);

            for (var i = 0; i < item.Subtasks.Count; i++)
            {
                db.Subtasks.Add(new Subtask
                {
                    Id = Guid.NewGuid(), TaskItemId = row.Id, Title = item.Subtasks[i].Title,
                    IsDone = item.Subtasks[i].IsDone, SortOrder = i, UpdatedAtUtc = now, ServerModifiedAtUtc = now
                });
            }
        }

        foreach (var item in doc.Habits)
        {
            var row = new Habit { Id = Guid.NewGuid(), UserId = userId, CreatedAtUtc = now };
            FillHabit(row, item, item.CategoryId is { } c && map.TryGetValue(c, out var mapped) ? mapped : null);
            Stamp(row, now);
            db.Habits.Add(row);

            foreach (var log in item.Logs)
            {
                db.HabitLogs.Add(new HabitLog
                {
                    HabitId = row.Id, Date = log.Date, CompletedAtUtc = log.CompletedAtUtc,
                    UpdatedAtUtc = now, ServerModifiedAtUtc = now
                });
            }
        }

        await db.SaveChangesAsync(ct);
        return new ImportResultDto(createdCategories, doc.Tasks.Count, doc.Habits.Count, skipped, []);
    }

    private async Task EnsureNoForeignIdsAsync(Guid userId, BackupDocument doc, CancellationToken ct)
    {
        var categoryIds = doc.Categories.Select(c => c.Id).ToList();
        var taskIds = doc.Tasks.Select(t => t.Id).ToList();
        var habitIds = doc.Habits.Select(h => h.Id).ToList();
        var subtaskIds = doc.Tasks.SelectMany(t => t.Subtasks).Select(s => s.Id).ToList();

        var foreign =
            (categoryIds.Count > 0 && await db.Categories.AnyAsync(c => categoryIds.Contains(c.Id) && c.UserId != userId, ct))
            || (taskIds.Count > 0 && await db.Tasks.AnyAsync(t => taskIds.Contains(t.Id) && t.UserId != userId, ct))
            || (habitIds.Count > 0 && await db.Habits.AnyAsync(h => habitIds.Contains(h.Id) && h.UserId != userId, ct))
            || (subtaskIds.Count > 0 && await db.Subtasks.AnyAsync(
                s => subtaskIds.Contains(s.Id) && !db.Tasks.Any(t => t.Id == s.TaskItemId && t.UserId == userId), ct));

        if (foreign)
        {
            throw new AppException(StatusCodes.Status409Conflict, "The backup contains items that belong to another account.");
        }
    }

    private void SyncSubtasks(TaskItem task, List<BackupSubtask> incoming, DateTime now)
    {
        var incomingIds = incoming.Select(s => s.Id).ToHashSet();
        foreach (var removed in task.Subtasks.Where(s => !incomingIds.Contains(s.Id)).ToList())
        {
            db.Subtasks.Remove(removed);
        }

        for (var order = 0; order < incoming.Count; order++)
        {
            var item = incoming[order];
            var existing = task.Subtasks.FirstOrDefault(s => s.Id == item.Id);
            if (existing is null)
            {
                db.Subtasks.Add(new Subtask
                {
                    Id = item.Id, TaskItemId = task.Id, Title = item.Title, IsDone = item.IsDone, SortOrder = order,
                    UpdatedAtUtc = now, ServerModifiedAtUtc = now
                });
            }
            else
            {
                existing.Title = item.Title;
                existing.IsDone = item.IsDone;
                existing.SortOrder = order;
                existing.UpdatedAtUtc = now;
                existing.ServerModifiedAtUtc = now;
            }
        }
    }

    private void SyncLogs(Habit habit, List<BackupHabitLog> incoming, DateTime now)
    {
        var dates = incoming.Select(l => l.Date).ToHashSet();
        foreach (var removed in habit.Logs.Where(l => !dates.Contains(l.Date)).ToList())
        {
            db.HabitLogs.Remove(removed);
        }

        foreach (var item in incoming)
        {
            var existing = habit.Logs.FirstOrDefault(l => l.Date == item.Date);
            if (existing is null)
            {
                db.HabitLogs.Add(new HabitLog
                {
                    HabitId = habit.Id, Date = item.Date, CompletedAtUtc = item.CompletedAtUtc,
                    UpdatedAtUtc = now, ServerModifiedAtUtc = now
                });
            }
            else
            {
                existing.CompletedAtUtc = item.CompletedAtUtc;
            }
        }
    }

    private static void FillTask(TaskItem row, BackupTask item, Guid? categoryId)
    {
        row.Title = item.Title;
        row.Description = item.Description;
        row.DeadlineUtc = item.DeadlineUtc;
        row.Priority = item.Priority;
        row.CategoryId = categoryId;
        row.ReminderAtUtc = item.ReminderAtUtc;
        row.CompletedAtUtc = item.CompletedAtUtc;
        row.EstimatedMinutes = item.EstimatedMinutes;
    }

    private static void FillHabit(Habit row, BackupHabit item, Guid? categoryId)
    {
        row.Title = item.Title;
        row.Description = item.Description;
        row.FrequencyType = item.FrequencyType;
        row.DaysOfWeekMask = item.DaysOfWeekMask;
        row.TimesPerWeek = item.TimesPerWeek;
        row.ReminderTime = item.ReminderTime;
        row.ColorHex = item.ColorHex;
        row.Icon = item.Icon;
        row.CategoryId = categoryId;
    }

    private static void Stamp(BaseEntity entity, DateTime now)
    {
        entity.IsDeleted = false;
        entity.UpdatedAtUtc = now;
        entity.ServerModifiedAtUtc = now;
    }

    private static void Tombstone(BaseEntity entity, DateTime now)
    {
        entity.IsDeleted = true;
        entity.UpdatedAtUtc = now;
        entity.ServerModifiedAtUtc = now;
    }

    // ---------- Перевірка й нормалізація вхідного документа ----------

    /// <summary>Відкидає некоректні записи, обрізає текст до довжин схеми БД; skipped — кількість відкинутих.</summary>
    internal static BackupDocument Normalize(BackupDocument source, out int skipped)
    {
        skipped = 0;

        var categories = new List<BackupCategory>();
        var seenCategories = new HashSet<Guid>();
        foreach (var c in source.Categories)
        {
            var name = CategoryNames.Normalize(c.Name);
            if (name.Length == 0 || c.Id == Guid.Empty || !seenCategories.Add(c.Id))
            {
                skipped++;
                continue;
            }

            categories.Add(c with { Name = Cut(name, 100), ColorHex = Color(c.ColorHex), Icon = CutOrNull(c.Icon, 50) });
        }

        var tasks = new List<BackupTask>();
        var seenTasks = new HashSet<Guid>();
        var seenSubtasks = new HashSet<Guid>();
        foreach (var t in source.Tasks)
        {
            var title = (t.Title ?? string.Empty).Trim();
            if (title.Length == 0 || t.Id == Guid.Empty || !seenTasks.Add(t.Id))
            {
                skipped++;
                continue;
            }

            var subtasks = new List<BackupSubtask>();
            foreach (var s in t.Subtasks ?? [])
            {
                var subtitle = (s.Title ?? string.Empty).Trim();
                if (subtitle.Length == 0 || s.Id == Guid.Empty || !seenSubtasks.Add(s.Id))
                {
                    continue;
                }

                subtasks.Add(s with { Title = Cut(subtitle, 200), SortOrder = subtasks.Count });
            }

            tasks.Add(t with
            {
                Title = Cut(title, 200),
                Description = CutOrNull(t.Description, 2000),
                DeadlineUtc = SyncTime.ToUtc(t.DeadlineUtc),
                ReminderAtUtc = SyncTime.ToUtc(t.ReminderAtUtc),
                CompletedAtUtc = SyncTime.ToUtc(t.CompletedAtUtc),
                Priority = Enum.IsDefined(t.Priority) ? t.Priority : TaskPriority.Medium,
                EstimatedMinutes = t.EstimatedMinutes is >= 1 and <= 1440 ? t.EstimatedMinutes : null,
                Subtasks = subtasks
            });
        }

        var habits = new List<BackupHabit>();
        var seenHabits = new HashSet<Guid>();
        foreach (var h in source.Habits)
        {
            var title = (h.Title ?? string.Empty).Trim();
            if (title.Length == 0 || h.Id == Guid.Empty || !seenHabits.Add(h.Id))
            {
                skipped++;
                continue;
            }

            var frequency = Enum.IsDefined(h.FrequencyType) ? h.FrequencyType : HabitFrequencyType.Daily;
            var logs = (h.Logs ?? [])
                .DistinctBy(l => l.Date)
                .Select(l => l with { CompletedAtUtc = SyncTime.ToUtc(l.CompletedAtUtc) })
                .ToList();

            habits.Add(h with
            {
                Title = Cut(title, 200),
                Description = CutOrNull(h.Description, 2000),
                FrequencyType = frequency,
                DaysOfWeekMask = Math.Clamp(h.DaysOfWeekMask, 0, 127),
                TimesPerWeek = frequency == HabitFrequencyType.TimesPerWeek ? Math.Clamp(h.TimesPerWeek ?? 1, 1, 7) : null,
                ColorHex = Color(h.ColorHex),
                Icon = CutOrNull(h.Icon, 50),
                Logs = logs
            });
        }

        if (categories.Count + tasks.Count + habits.Count > BackupFormat.MaxItems)
        {
            throw new AppException(StatusCodes.Status400BadRequest, $"The file contains more than {BackupFormat.MaxItems} items.");
        }

        return source with { Categories = categories, Tasks = tasks, Habits = habits };
    }

    private static string Cut(string value, int max) => value.Length <= max ? value : value[..max];

    private static string? CutOrNull(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : Cut(value.Trim(), max);

    private static string? Color(string? hex) => hex is not null && ColorPattern().IsMatch(hex) ? hex : null;

    [GeneratedRegex("^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$")]
    private static partial Regex ColorPattern();
}