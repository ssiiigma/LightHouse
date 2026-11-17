using Lighthouse.Mobile.Data;
using Lighthouse.Mobile.Infrastructure;
using Lighthouse.Mobile.Models;
using Lighthouse.Mobile.Services.Api;
using Lighthouse.Shared.Analytics;
using Lighthouse.Mobile.Localization;

namespace Lighthouse.Mobile.Services.Analytics;

public sealed record AnalyticsData(AnalyticsResult Result, IReadOnlyDictionary<Guid, CategoryModel> Categories);

/// <summary>Аналітика обчислюється на пристрої за SQLite (офлайн); звіт PDF/CSV формує сервер і потребує мережі.</summary>
public interface IAnalyticsSvc
{
    Task<AnalyticsData> GetAsync(AnalyticsPeriod period);

    /// <summary>Завантажує звіт з сервера в кеш застосунку й повертає шлях до файлу.</summary>
    Task<string> DownloadReportAsync(string format, DateRange range, CancellationToken ct);
}

public sealed class AnalyticsService(ILocalDB localDb, IApiClient api) : IAnalyticsSvc
{
    public async Task<AnalyticsData> GetAsync(AnalyticsPeriod period)
    {
        var db = await localDb.GetConnectionAsync();
        var taskRows = await db.Table<LocalTask>().Where(t => !t.IsDeleted).ToListAsync();
        var habitRows = await db.Table<LocalHabit>().Where(h => !h.IsDeleted).ToListAsync();
        var logRows = await db.Table<LocalHabitLog>().ToListAsync();
        var categoryRows = await db.Table<LocalCategory>().Where(c => !c.IsDeleted).ToListAsync();

        var tasks = taskRows
            .Select(t => new AnalyticsTask(t.CategoryId, ToLocal(t.DeadlineUtc), ToLocal(t.CompletedAtUtc)))
            .ToList();

        var logsByHabit = logRows
            .GroupBy(l => l.HabitId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<DateOnly>)g.Select(l => HabitKeys.Parse(l.Date)).ToList());

        var habits = habitRows
            .Select(h => new AnalyticsHabit(
                h.FrequencyType, h.DaysOfWeekMask, h.TimesPerWeek,
                logsByHabit.GetValueOrDefault(h.Id) ?? []))
            .ToList();

        var result = AnalyticsCalculator.Calculate(period, DateTime.Now, tasks, habits);
        var categories = categoryRows.ToDictionary(
            c => c.Id, c => new CategoryModel { Id = c.Id, Name = c.Name, ColorHex = c.ColorHex });

        return new AnalyticsData(result, categories);
    }

    public async Task<string> DownloadReportAsync(string format, DateRange range, CancellationToken ct)
    {
        var path = $"api/reports?format={format}&from={HabitKeys.Format(range.From)}&to={HabitKeys.Format(range.To)}" +
                   $"&lang={LocalizationService.Instance.Language}";
        var bytes = await api.GetBytesAsync(path, ct);

        var file = Path.Combine(FileSystem.CacheDirectory,
            $"lighthouse-report-{HabitKeys.Format(range.From)}_{HabitKeys.Format(range.To)}.{format}");
        await File.WriteAllBytesAsync(file, bytes, ct);
        return file;
    }

    private static DateTime? ToLocal(DateTime? utc) =>
        utc is { } value ? DateTime.SpecifyKind(value, DateTimeKind.Utc).ToLocalTime() : null;
}