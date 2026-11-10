using Lighthouse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Lighthouse.Api.Infrastructure;

/// <summary>Нормалізація часу для синхронізації: усі моменти на сервері — UTC.</summary>
public static class SyncTime
{
    /// <summary>
    /// Local → UTC (так модель-біндінг ASP.NET перетворює «...Z» у query), Unspecified вважається UTC.
    /// </summary>
    public static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    public static DateTime? ToUtc(DateTime? value) => value is { } v ? ToUtc(v) : null;

    /// <summary>Час клієнта, обмежений часом сервера (захист від годинника «з майбутнього»).</summary>
    public static DateTime ClampToNow(DateTime clientValue, DateTime nowUtc)
    {
        var utc = ToUtc(clientValue);
        return utc > nowUtc ? nowUtc : utc;
    }
}

public interface IUserTimeService
{
    /// <summary>Часовий пояс користувача (User.TimeZoneId); якщо не заданий або невідомий — UTC.</summary>
    Task<TimeZoneInfo> GetZoneAsync(Guid userId, CancellationToken ct);
}

public sealed class UserTimeService(LighthouseDbContext db) : IUserTimeService
{
    private readonly Dictionary<Guid, TimeZoneInfo> _cache = new();

    public async Task<TimeZoneInfo> GetZoneAsync(Guid userId, CancellationToken ct)
    {
        if (_cache.TryGetValue(userId, out var cached))
        {
            return cached;
        }

        var id = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.TimeZoneId)
            .FirstOrDefaultAsync(ct);

        var zone = Resolve(id);
        _cache[userId] = zone;
        return zone;
    }

    private static TimeZoneInfo Resolve(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}

public static class UserTime
{
    public static DateTime ToLocal(DateTime utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(SyncTime.ToUtc(utc), zone);

    public static DateOnly Today(TimeZoneInfo zone, DateTime nowUtc) =>
        DateOnly.FromDateTime(ToLocal(nowUtc, zone));

    /// <summary>День тижня: понеділок = 0 … неділя = 6 (збігається з бітами Habit.DaysOfWeekMask).</summary>
    public static int DayIndex(DateTime local) => ((int)local.DayOfWeek + 6) % 7;
}