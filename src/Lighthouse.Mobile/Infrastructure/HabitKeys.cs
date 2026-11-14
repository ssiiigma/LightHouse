using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lighthouse.Mobile.Infrastructure;

/// <summary>Вміст операції черги для відмітки звички (ідентифікатор відмітки не містить звичку й дату, тож вони в навантаженні).</summary>
internal sealed record HabitLogPayload(Guid HabitId, string Date, DateTime? CompletedAtUtc);

internal static class HabitKeys
{
    /// <summary>Детермінований ідентифікатор відмітки з пари «звичка + дата» (не криптографічне використання MD5).</summary>
    public static Guid LogId(Guid habitId, string date) =>
        new(MD5.HashData(Encoding.UTF8.GetBytes($"{habitId:N}|{date}")));

    public static string Format(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static DateOnly Parse(string date) =>
        DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static HabitLogPayload? ReadPayload(string? json) =>
        string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<HabitLogPayload>(json, AppJson.Options);
}