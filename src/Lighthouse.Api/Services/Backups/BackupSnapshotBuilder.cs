using Lighthouse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Lighthouse.Api.Services.Backups;

/// <summary>Збирає знімок даних користувача (без видалених записів) у документ копії.</summary>
public sealed class BackupSnapshotBuilder(LighthouseDbContext db)
{
    public async Task<BackupDocument> BuildAsync(Guid userId, DateTime nowUtc, CancellationToken ct)
    {
        var timeZone = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.TimeZoneId).FirstOrDefaultAsync(ct);
        var categories = await db.Categories.AsNoTracking()
            .Where(c => c.UserId == userId && !c.IsDeleted).OrderBy(c => c.Name).ToListAsync(ct);
        var tasks = await db.Tasks.AsNoTracking().Include(t => t.Subtasks)
            .Where(t => t.UserId == userId && !t.IsDeleted).OrderBy(t => t.CreatedAtUtc).ToListAsync(ct);
        var habits = await db.Habits.AsNoTracking().Include(h => h.Logs)
            .Where(h => h.UserId == userId && !h.IsDeleted).OrderBy(h => h.CreatedAtUtc).ToListAsync(ct);

        var known = categories.Select(c => c.Id).ToHashSet();
        Guid? Category(Guid? id) => id is { } value && known.Contains(value) ? value : null;

        return new BackupDocument(
            BackupDocument.CurrentVersion, nowUtc, timeZone,
            categories.Select(c => new BackupCategory(c.Id, c.Name, c.ColorHex, c.Icon)).ToList(),
            tasks.Select(t => new BackupTask(
                t.Id, t.Title, t.Description, t.DeadlineUtc, t.Priority, Category(t.CategoryId),
                t.ReminderAtUtc, t.CompletedAtUtc, t.EstimatedMinutes,
                t.Subtasks.OrderBy(s => s.SortOrder).Select(s => new BackupSubtask(s.Id, s.Title, s.IsDone, s.SortOrder)).ToList())).ToList(),
            habits.Select(h => new BackupHabit(
                h.Id, h.Title, h.Description, h.FrequencyType, h.DaysOfWeekMask, h.TimesPerWeek, h.ReminderTime,
                h.ColorHex, h.Icon, Category(h.CategoryId),
                h.Logs.OrderBy(l => l.Date).Select(l => new BackupHabitLog(l.Date, l.CompletedAtUtc)).ToList())).ToList());
    }
}