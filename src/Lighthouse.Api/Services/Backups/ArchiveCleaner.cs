using Lighthouse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Lighthouse.Api.Services.Backups;

public sealed record CleanupResult(int Tasks, int Habits, int Categories, int Tokens);

/// <summary>
/// Остаточне видалення «м'яко» видалених записів після 14 діб архіву (разом із підзавданнями, відмітками звичок і
/// зв'язками з подіями календаря) та прострочених службових токенів.
/// </summary>
public sealed class ArchiveCleaner(LighthouseDbContext db)
{
    public const int ArchiveDays = 14;
    private const int Chunk = 500;

    public async Task<CleanupResult> PurgeAsync(DateTime nowUtc, CancellationToken ct)
    {
        var cutoff = nowUtc.AddDays(-ArchiveDays);
        int tasks = 0, habits = 0, categories = 0;

        var taskIds = await db.Tasks.Where(t => t.IsDeleted && t.ServerModifiedAtUtc < cutoff).Select(t => t.Id).ToListAsync(ct);
        foreach (var ids in taskIds.Chunk(Chunk))
        {
            await db.CalendarEventLinks.Where(l => ids.Contains(l.TaskId)).ExecuteDeleteAsync(ct);
            tasks += await db.Tasks.Where(t => ids.Contains(t.Id)).ExecuteDeleteAsync(ct); // підзавдання — каскадом
        }

        var habitIds = await db.Habits.Where(h => h.IsDeleted && h.ServerModifiedAtUtc < cutoff).Select(h => h.Id).ToListAsync(ct);
        foreach (var ids in habitIds.Chunk(Chunk))
        {
            habits += await db.Habits.Where(h => ids.Contains(h.Id)).ExecuteDeleteAsync(ct); // відмітки — каскадом
        }

        var categoryIds = await db.Categories.Where(c => c.IsDeleted && c.ServerModifiedAtUtc < cutoff).Select(c => c.Id).ToListAsync(ct);
        foreach (var ids in categoryIds.Chunk(Chunk))
        {
            // Зовнішні ключі без каскаду: спершу прибираємо посилання.
            await db.Tasks.Where(t => t.CategoryId != null && ids.Contains(t.CategoryId.Value))
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.CategoryId, (Guid?)null), ct);
            await db.Habits.Where(h => h.CategoryId != null && ids.Contains(h.CategoryId.Value))
                .ExecuteUpdateAsync(s => s.SetProperty(h => h.CategoryId, (Guid?)null), ct);
            categories += await db.Categories.Where(c => ids.Contains(c.Id)).ExecuteDeleteAsync(ct);
        }

        var tokenCutoff = nowUtc.AddDays(-30);
        var emailCutoff = nowUtc.AddDays(-7);
        var tokens = await db.EmailTokens.Where(t => t.ExpiresAtUtc < emailCutoff).ExecuteDeleteAsync(ct)
                     + await db.RefreshTokens
                         .Where(t => t.ExpiresAtUtc < tokenCutoff || (t.RevokedAtUtc != null && t.RevokedAtUtc < tokenCutoff))
                         .ExecuteDeleteAsync(ct);

        return new CleanupResult(tasks, habits, categories, tokens);
    }
}