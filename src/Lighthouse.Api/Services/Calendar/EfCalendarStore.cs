using Lighthouse.Api.Data;
using Lighthouse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Lighthouse.Api.Services.Calendar;

/// <summary>
/// Єдине місце, що знає про сутності CalendarConnection і CalendarEventLink. Використані властивості:
/// CalendarConnection.{UserId, RefreshTokenEncrypted, CalendarId}; CalendarEventLink.{TaskId, GoogleEventId, SyncedAtUtc}.
/// </summary>
public sealed class EfCalendarStore(LighthouseDbContext db, ICalendarTokenProtector protector) : ICalendarStore
{
    public async Task<bool> IsConnectedAsync(Guid userId, CancellationToken ct) =>
        await db.CalendarConnections.AsNoTracking().AnyAsync(c => c.UserId == userId, ct);

    public async Task<CalendarConnectionInfo?> GetConnectionAsync(Guid userId, CancellationToken ct)
    {
        var row = await db.CalendarConnections.AsNoTracking().FirstOrDefaultAsync(c => c.UserId == userId, ct);
        return row is null
            ? null
            : new CalendarConnectionInfo(
                userId, protector.Unprotect(row.RefreshTokenEncrypted),
                string.IsNullOrEmpty(row.CalendarId) ? "primary" : row.CalendarId);
    }

        /// <summary>
    /// Якщо первинний ключ нового запису лишився порожнім (ключ не генерується автоматично), призначає новий Guid.
    /// Ключ, що збігається з природним (наприклад, UserId або TaskId), не чіпається.
    /// </summary>
    private static void EnsureGuidKey(EntityEntry entry, string naturalKey)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is not { Properties.Count: 1 })
        {
            return;
        }

        var property = key.Properties[0];
        if (property.ClrType != typeof(Guid) || property.Name == naturalKey)
        {
            return;
        }

        var current = entry.Property(property.Name);
        if (current.CurrentValue is Guid value && value == Guid.Empty)
        {
            current.CurrentValue = Guid.NewGuid();
        }
    }

    public async Task SaveConnectionAsync(Guid userId, string refreshToken, string calendarId, CancellationToken ct)
    {
        var row = await db.CalendarConnections.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (row is null)
        {
            row = new CalendarConnection { UserId = userId };
            EnsureGuidKey(db.CalendarConnections.Add(row), nameof(CalendarConnection.UserId));
        }

        row.RefreshTokenEncrypted = protector.Protect(refreshToken); // у БД лише зашифрований токен
        row.CalendarId = calendarId;
        await SaveAsync(ct);
    }

        public async Task UpsertLinkAsync(Guid taskId, string eventId, DateTime syncedAtUtc, CancellationToken ct)
    {
        var row = await db.CalendarEventLinks.FirstOrDefaultAsync(l => l.TaskId == taskId, ct);
        if (row is null)
        {
            row = new CalendarEventLink { TaskId = taskId };
            var entry = db.CalendarEventLinks.Add(row);
            await PrepareLinkAsync(entry, taskId, ct);
        }

        row.GoogleEventId = eventId;
        row.SyncedAtUtc = syncedAtUtc;
        await SaveAsync(ct);
    }

    /// <summary>
    /// Заповнює обов'язкові значення нового зв'язку за метаданими EF: порожній ключ, зовнішні ключі на завдання,
    /// користувача чи підключення календаря та обов'язкові рядки.
    /// </summary>
    private async Task PrepareLinkAsync(EntityEntry entry, Guid taskId, CancellationToken ct)
    {
        EnsureGuidKey(entry, nameof(CalendarEventLink.TaskId));

        foreach (var foreignKey in entry.Metadata.GetForeignKeys())
        {
            if (foreignKey.Properties.Count != 1 || foreignKey.Properties[0].ClrType != typeof(Guid))
            {
                continue;
            }

            var property = entry.Property(foreignKey.Properties[0].Name);
            if (property.CurrentValue is not Guid current || current != Guid.Empty)
            {
                continue;
            }

            var principal = foreignKey.PrincipalEntityType.ClrType;
            if (principal == typeof(TaskItem))
            {
                property.CurrentValue = taskId;
                continue;
            }

            var userId = await db.Tasks.Where(t => t.Id == taskId).Select(t => t.UserId).FirstAsync(ct);
            if (principal == typeof(User))
            {
                property.CurrentValue = userId;
            }
            else if (principal == typeof(CalendarConnection))
            {
                var connection = await db.CalendarConnections.AsNoTracking().FirstAsync(c => c.UserId == userId, ct);
                var connectionEntry = db.Entry(connection);
                var keyName = connectionEntry.Metadata.FindPrimaryKey()!.Properties[0].Name;
                property.CurrentValue = connectionEntry.Property(keyName).CurrentValue;
            }
        }

        foreach (var property in entry.Metadata.GetProperties())
        {
            if (property.ClrType == typeof(string) && !property.IsNullable
                && entry.Property(property.Name).CurrentValue is null)
            {
                entry.Property(property.Name).CurrentValue = string.Empty;
            }
        }
    }

    /// <summary>Після невдалого збереження контекст очищається, щоб помилковий запис не ламав наступні операції.</summary>
    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception)
        {
            db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<IReadOnlyList<Guid>> ConnectedUserIdsAsync(CancellationToken ct) =>
        await db.CalendarConnections.AsNoTracking().Select(c => c.UserId).ToListAsync(ct);

    public async Task<IReadOnlyList<CalendarTaskInfo>> GetTasksAsync(Guid userId, CancellationToken ct)
    {
        var linked = db.CalendarEventLinks.Select(l => l.TaskId);
        var rows = await db.Tasks.AsNoTracking()
            .Where(t => t.UserId == userId
                        && ((!t.IsDeleted && t.CompletedAtUtc == null && t.DeadlineUtc != null) || linked.Contains(t.Id)))
            .ToListAsync(ct);

        return rows.Select(t => new CalendarTaskInfo(
            t.Id, t.Title, t.Description, t.DeadlineUtc, t.ReminderAtUtc, t.EstimatedMinutes,
            t.CompletedAtUtc is not null, t.IsDeleted, t.ServerModifiedAtUtc)).ToList();
    }

    public async Task<IReadOnlyList<CalendarLinkInfo>> GetLinksAsync(Guid userId, CancellationToken ct)
    {
        var rows = await (from l in db.CalendarEventLinks.AsNoTracking()
                          join t in db.Tasks on l.TaskId equals t.Id
                          where t.UserId == userId
                          select new { l.TaskId, l.GoogleEventId, l.SyncedAtUtc }).ToListAsync(ct);

        return rows.Select(r => new CalendarLinkInfo(r.TaskId, r.GoogleEventId, r.SyncedAtUtc)).ToList();
    }

    public async Task RemoveLinkAsync(Guid taskId, CancellationToken ct) =>
        await db.CalendarEventLinks.Where(l => l.TaskId == taskId).ExecuteDeleteAsync(ct);

    public async Task RemoveConnectionAsync(Guid userId, CancellationToken ct)
    {
        await db.CalendarEventLinks
            .Where(l => db.Tasks.Any(t => t.Id == l.TaskId && t.UserId == userId))
            .ExecuteDeleteAsync(ct);
        await db.CalendarConnections.Where(c => c.UserId == userId).ExecuteDeleteAsync(ct);
    }
}