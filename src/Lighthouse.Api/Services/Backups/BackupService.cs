using Lighthouse.Api.Data;
using Lighthouse.Api.Infrastructure;
using Lighthouse.Core.Entities;
using Lighthouse.Shared.Contracts.Backups;
using Lighthouse.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Lighthouse.Api.Services.Backups;

public interface IBackupService
{
    Task<IReadOnlyList<BackupInfoDto>> ListAsync(Guid userId, CancellationToken ct);

    /// <param name="enforceLimit">Для ручних копій з API: не більше 10; службові (перед відновленням) ліміт обходять.</param>
    Task<BackupInfoDto> CreateAsync(
        Guid userId, BackupKind kind, bool enforceLimit, DateTime? createdAtUtc, CancellationToken ct);

    Task<(byte[] Content, string FileName)> DownloadAsync(Guid userId, Guid id, CancellationToken ct);
    Task<RestoreResultDto> RestoreAsync(Guid userId, Guid id, CancellationToken ct);
    Task DeleteAsync(Guid userId, Guid id, CancellationToken ct);
}

public sealed class BackupService(
    LighthouseDbContext db, BackupSnapshotBuilder snapshots, BackupApplier applier) : IBackupService
{
    public const int ManualLimit = 10;
    public const int AutoRetention = 7;

    public async Task<IReadOnlyList<BackupInfoDto>> ListAsync(Guid userId, CancellationToken ct) =>
        await db.Backups.AsNoTracking()
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.CreatedAtUtc)
            .Select(b => new BackupInfoDto(b.Id, b.CreatedAtUtc, b.Kind, b.SizeBytes))
            .ToListAsync(ct);

    public async Task<BackupInfoDto> CreateAsync(
        Guid userId, BackupKind kind, bool enforceLimit, DateTime? createdAtUtc, CancellationToken ct)
    {
        if (kind == BackupKind.Manual && enforceLimit
            && await db.Backups.CountAsync(b => b.UserId == userId && b.Kind == BackupKind.Manual, ct) >= ManualLimit)
        {
            throw new AppException(StatusCodes.Status409Conflict,
                $"The limit of {ManualLimit} manual backups is reached. Delete an old copy first.");
        }

        var now = createdAtUtc ?? DateTime.UtcNow;
        var document = await snapshots.BuildAsync(userId, now, ct);
        var compressed = BackupFormat.Gzip(BackupFormat.ToJson(document));

        var entity = new Backup
        {
            UserId = userId, CreatedAtUtc = now, Kind = kind, SizeBytes = compressed.Length, Content = compressed
        };
        db.Backups.Add(entity);
        await db.SaveChangesAsync(ct);

        if (kind == BackupKind.Auto)
        {
            await PruneAutoAsync(userId, ct);
        }

        return new BackupInfoDto(entity.Id, entity.CreatedAtUtc, entity.Kind, entity.SizeBytes);
    }

    public async Task<(byte[] Content, string FileName)> DownloadAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var backup = await FindAsync(userId, id, ct);
        return (BackupFormat.Gunzip(backup.Content), $"lighthouse-backup-{backup.CreatedAtUtc:yyyy-MM-dd-HHmm}.json");
    }

    public async Task<RestoreResultDto> RestoreAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var backup = await FindAsync(userId, id, ct);
        var document = BackupFormat.ParseJson(BackupFormat.Gunzip(backup.Content));

        var safety = await CreateAsync(userId, BackupKind.Manual, enforceLimit: false, createdAtUtc: null, ct);
        db.ChangeTracker.Clear();

        var (categories, tasks, habits) = await applier.ReplaceAsync(userId, document, ct);
        return new RestoreResultDto(categories, tasks, habits, safety.Id);
    }

    public async Task DeleteAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var backup = await FindAsync(userId, id, ct);
        db.Backups.Remove(backup);
        await db.SaveChangesAsync(ct);
    }

    private async Task<Backup> FindAsync(Guid userId, Guid id, CancellationToken ct) =>
        await db.Backups.FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId, ct)
        ?? throw new AppException(StatusCodes.Status404NotFound, "Backup not found.");

    /// <summary>Зберігаються 7 останніх автоматичних копій.</summary>
    private async Task PruneAutoAsync(Guid userId, CancellationToken ct)
    {
        var stale = await db.Backups
            .Where(b => b.UserId == userId && b.Kind == BackupKind.Auto)
            .OrderByDescending(b => b.CreatedAtUtc)
            .Skip(AutoRetention)
            .Select(b => b.Id)
            .ToListAsync(ct);

        if (stale.Count > 0)
        {
            await db.Backups.Where(b => stale.Contains(b.Id)).ExecuteDeleteAsync(ct);
        }
    }
}