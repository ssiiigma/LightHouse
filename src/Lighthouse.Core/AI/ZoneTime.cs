namespace Lighthouse.Core.AI;

internal static class ZoneTime
{
    public static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    /// <summary>Початок локальної доби (00:00) у UTC; якщо опівночі не існує (перехід на літній час) — наступна година.</summary>
    public static DateTime LocalMidnightToUtc(DateTime localDate, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(localDate.Date, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(unspecified))
        {
            unspecified = unspecified.AddHours(1);
        }

        return TimeZoneInfo.ConvertTimeToUtc(unspecified, zone);
    }
}