using Lighthouse.Api.Data;
using Lighthouse.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Lighthouse.Api.Services.Backups;

/// <summary>Щодобова автоматична копія для користувачів, дані яких змінились після попередньої автоматичної копії.</summary>
public sealed class BackupScheduler(LighthouseDbContext db, IBackupService backups, ILogger<BackupScheduler> logger)
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    public async Task<int> RunAsync(DateTime nowUtc, CancellationToken ct)
    {
        var userIds = await db.Users.AsNoTracking().Where(u => !u.IsDeleted).Select(u => u.Id).ToListAsync(ct);
        var created = 0;

        foreach (var userId in userIds)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var lastAuto = await db.Backups
                    .Where(b => b.UserId == userId && b.Kind == BackupKind.Auto)
                    .MaxAsync(b => (DateTime?)b.CreatedAtUtc, ct);

                if (lastAuto is { } last && nowUtc - last < Interval)
                {
                    continue;
                }

                var since = lastAuto ?? DateTime.MinValue;
                var changed =
                    await db.Tasks.AnyAsync(t => t.UserId == userId && t.ServerModifiedAtUtc > since, ct)
                    || await db.Habits.AnyAsync(h => h.UserId == userId && h.ServerModifiedAtUtc > since, ct)
                    || await db.Categories.AnyAsync(c => c.UserId == userId && c.ServerModifiedAtUtc > since, ct);
                if (!changed)
                {
                    continue;
                }

                await backups.CreateAsync(userId, BackupKind.Auto, enforceLimit: false, createdAtUtc: nowUtc, ct);
                created++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Automatic backup failed for user {UserId}.", userId);
                db.ChangeTracker.Clear();
            }
        }

        return created;
    }
}