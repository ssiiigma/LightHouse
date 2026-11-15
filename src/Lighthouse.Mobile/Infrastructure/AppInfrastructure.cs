using System.Globalization;
using System.Text.Json;

namespace Lighthouse.Mobile.Infrastructure;

internal static class Utc
{
    public static DateTime Now => DateTime.UtcNow;

    /// <summary>SQLite повертає DateTime без Kind; усі збережені моменти — UTC.</summary>
    public static DateTime Ensure(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    public static DateTime? Ensure(DateTime? value) => value is { } v ? Ensure(v) : null;
}

internal static class AppJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

public static class EntityTypes
{
    public const string Task = "task";
    public const string Category = "category";
    public const string Habit = "habit";
    public const string HabitLog = "habitlog";
    public const string Feedback = "feedback";

    public static readonly string[] All = [Task, Category, Habit, HabitLog, Feedback];
}

public static class PreferenceKeys
{
    public const string UserId = "user.id";
    public const string UserName = "user.name";
    public const string UserEmail = "user.email";
    public const string TimeZoneSent = "profile.timezone.sent";
}

/// <summary>Курсор дельта-синхронізації (максимальний ServerModifiedAtUtc отриманих записів) у Preferences.</summary>
internal static class SyncCursor
{
    private static string Key(string entityType) => $"sync.cursor.{entityType}";

    public static DateTime? Load(string entityType)
    {
        var text = Preferences.Default.Get(Key(entityType), string.Empty);
        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value)
            ? Utc.Ensure(value)
            : null;
    }

    public static void Save(string entityType, DateTime value) =>
        Preferences.Default.Set(Key(entityType), Utc.Ensure(value).ToString("O", CultureInfo.InvariantCulture));

    public static void ClearAll()
    {
        foreach (var entityType in EntityTypes.All)
        {
            Preferences.Default.Remove(Key(entityType));
        }
    }
}

/// <summary>Приховані поради: ідентифікатор поради → до якого моменту (UTC) не показувати.</summary>
internal static class AdviceStore
{
    private const string Key = "advice.suppressed";
    private static readonly object Gate = new();

    public static bool IsSuppressed(string id)
    {
        lock (Gate)
        {
            return Load().TryGetValue(id, out var until) && until > Utc.Now;
        }
    }

    public static void Suppress(string id, TimeSpan duration)
    {
        lock (Gate)
        {
            var map = Load();
            var now = Utc.Now;
            foreach (var expired in map.Where(p => p.Value <= now).Select(p => p.Key).ToList())
            {
                map.Remove(expired);
            }

            map[id] = now + duration;
            Preferences.Default.Set(Key, JsonSerializer.Serialize(map));
        }
    }

    public static void Clear() => Preferences.Default.Remove(Key);

    private static Dictionary<string, DateTime> Load()
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, DateTime>>(Preferences.Default.Get(Key, "{}")) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }
}