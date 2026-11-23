using Lighthouse.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Lighthouse.Api.Services.Push;

/// <summary>
/// Журнал push у таблиці NotificationQueue. Очікувані властивості: UserId, Kind, SentAtUtc (nullable), ScheduledAtUtc,
/// TaskId (nullable); відсутні при запису пропускаються, обов'язкові рядки заповнюються порожніми.
/// </summary>
public sealed class EfPushLog(LighthouseDbContext db) : IPushLog
{
    public async Task<bool> SentBetweenAsync(Guid userId, PushKind kind, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        var value = (int)kind;
        return await db.NotificationQueue.AsNoTracking().AnyAsync(
            n => n.UserId == userId && (int)n.Kind == value
                 && n.SentAtUtc != null && n.SentAtUtc >= fromUtc && n.SentAtUtc < toUtc, ct);
    }

    public async Task RecordAsync(Guid userId, PushKind kind, Guid? taskId, DateTime sentAtUtc, CancellationToken ct)
    {
        var entry = db.Add(Activator.CreateInstance(db.NotificationQueue.EntityType.ClrType)!);

        EnsureGuidKey(entry);
        Set(entry, "UserId", userId);
        Set(entry, "Kind", KindValue(entry, kind));
        Set(entry, "SentAtUtc", sentAtUtc);
        Set(entry, "ScheduledAtUtc", sentAtUtc);
        Set(entry, "TaskId", taskId);

        foreach (var property in entry.Metadata.GetProperties())
        {
            if (property.ClrType == typeof(string) && !property.IsNullable && entry.Property(property.Name).CurrentValue is null)
            {
                entry.Property(property.Name).CurrentValue = string.Empty;
            }
        }

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

    private static void Set(EntityEntry entry, string name, object? value)
    {
        if (entry.Metadata.FindProperty(name) is not null)
        {
            entry.Property(name).CurrentValue = value;
        }
    }

    private static object KindValue(EntityEntry entry, PushKind kind)
    {
        var type = entry.Metadata.FindProperty("Kind")?.ClrType;
        type = Nullable.GetUnderlyingType(type ?? typeof(int)) ?? type ?? typeof(int);
        return type.IsEnum ? Enum.ToObject(type, (int)kind) : (int)kind;
    }

    private static void EnsureGuidKey(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is { Properties.Count: 1 } && key.Properties[0].ClrType == typeof(Guid))
        {
            var property = entry.Property(key.Properties[0].Name);
            if (property.CurrentValue is Guid value && value == Guid.Empty)
            {
                property.CurrentValue = Guid.NewGuid();
            }
        }
    }
}