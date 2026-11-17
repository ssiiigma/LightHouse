using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration.Attributes;
using Lighthouse.Api.Data;
using Lighthouse.Api.Infrastructure;
using Lighthouse.Shared.Analytics;
using Lighthouse.Shared.Enums;
using Lighthouse.Shared.Habits;
using Microsoft.EntityFrameworkCore;

namespace Lighthouse.Api.Services.Reports;

public sealed record ReportFile(byte[] Content, string ContentType, string FileName);

public interface IReportService
{
    /// <summary>Звіт за діапазон дат у часовому поясі користувача: PDF (підсумок) або CSV (перелік завдань).</summary>
    Task<ReportFile> BuildAsync(Guid userId, string format, DateOnly from, DateOnly to, string? language, CancellationToken ct);
}

internal sealed class TaskCsvRow
{
    [CsvHelper.Configuration.Attributes.Index(0)] public string Title { get; init; } = "";
    [CsvHelper.Configuration.Attributes.Index(1)] public string Category { get; init; } = "";
    [CsvHelper.Configuration.Attributes.Index(2)] public string Priority { get; init; } = "";
    [CsvHelper.Configuration.Attributes.Index(3)] public string Deadline { get; init; } = "";
    [CsvHelper.Configuration.Attributes.Index(4)] public string CompletedAt { get; init; } = "";
    [CsvHelper.Configuration.Attributes.Index(5)] public int? EstimatedMinutes { get; init; }
}

public sealed class ReportService(
    LighthouseDbContext db, IUserTimeService userTime, ILogger<ReportService> logger) : IReportService
{
    /// <summary>Найдовший діапазон звіту (рік із запасом).</summary>
    public const int MaxDays = 366;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public async Task<ReportFile> BuildAsync(
        Guid userId, string format, DateOnly from, DateOnly to, string? language, CancellationToken ct)
    {
        format = format.Trim().ToLowerInvariant();
        if (format is not ("pdf" or "csv"))
        {
            throw new AppException(StatusCodes.Status400BadRequest, "Format must be pdf or csv.");
        }

        if (to < from || to.DayNumber - from.DayNumber + 1 > MaxDays)
        {
            throw new AppException(StatusCodes.Status400BadRequest, $"The range must be 1–{MaxDays} days.");
        }

        var zone = await userTime.GetZoneAsync(userId, ct);
        var nowLocal = UserTime.ToLocal(DateTime.UtcNow, zone);
        var range = new DateRange(from, to);

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, ct)
                   ?? throw new AppException(StatusCodes.Status404NotFound, "User not found.");
        var taskRows = await db.Tasks.AsNoTracking().Where(t => t.UserId == userId && !t.IsDeleted).ToListAsync(ct);
        var habitRows = await db.Habits.AsNoTracking().Include(h => h.Logs)
            .Where(h => h.UserId == userId && !h.IsDeleted).ToListAsync(ct);
        var categoryNames = await db.Categories.AsNoTracking()
            .Where(c => c.UserId == userId && !c.IsDeleted)
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var strings = ReportStrings.For(language);
        var name = $"lighthouse-report-{from:yyyy-MM-dd}_{to:yyyy-MM-dd}";

        if (format == "csv")
        {
            return new ReportFile(
                await BuildCsvAsync(taskRows, categoryNames, range, zone, strings, ct), "text/csv", name + ".csv");
        }

        ReportFonts.EnsureInitialized(logger);

        var analyticsTasks = taskRows
            .Select(t => new AnalyticsTask(t.CategoryId, ToLocal(t.DeadlineUtc, zone), ToLocal(t.CompletedAtUtc, zone)))
            .ToList();
        var analyticsHabits = habitRows
            .Select(h => new AnalyticsHabit(
                h.FrequencyType, h.DaysOfWeekMask, h.TimesPerWeek, h.Logs.Select(l => l.Date).ToList()))
            .ToList();
        var summary = AnalyticsCalculator.Summarize(range, nowLocal, analyticsTasks, analyticsHabits);

        var today = DateOnly.FromDateTime(nowLocal);
        var habits = habitRows.OrderBy(h => h.Title).Select(h =>
        {
            var streaks = HabitStreakCalculator.Calculate(
                h.FrequencyType, h.DaysOfWeekMask, h.TimesPerWeek, h.Logs.Select(l => l.Date), today);
            return new ReportHabit(h.Title, h.Logs.Count(l => range.Contains(l.Date)), streaks.Current, streaks.Best);
        }).ToList();

        var completed = taskRows
            .Where(t => ToLocal(t.CompletedAtUtc, zone) is { } c && range.Contains(DateOnly.FromDateTime(c)))
            .Select(t => new ReportTask(
                ToLocal(t.CompletedAtUtc, zone)!.Value, t.Title, CategoryName(t.CategoryId, categoryNames, strings),
                PriorityName(t.Priority, strings)))
            .OrderBy(t => t.CompletedLocal)
            .ToList();

        var data = new ReportData(user.Name, summary, categoryNames, completed, habits, nowLocal);
        return new ReportFile(ReportPdf.Build(data, strings), "application/pdf", name + ".pdf");
    }

    /// <summary>CSV: завдання, виконані в діапазоні або відкриті з дедлайном у ньому. UTF-8 із BOM (коректно відкривається в Excel).</summary>
    private static async Task<byte[]> BuildCsvAsync(
        List<Core.Entities.TaskItem> tasks, Dictionary<Guid, string> categories, DateRange range,
        TimeZoneInfo zone, ReportStrings strings, CancellationToken ct)
    {
        var rows = tasks
            .Select(t => (Task: t, Completed: ToLocal(t.CompletedAtUtc, zone), Deadline: ToLocal(t.DeadlineUtc, zone)))
            .Where(x => x.Completed is { } c
                ? range.Contains(DateOnly.FromDateTime(c))
                : x.Deadline is { } d && range.Contains(DateOnly.FromDateTime(d)))
            .OrderBy(x => x.Completed ?? x.Deadline)
            .Select(x => new TaskCsvRow
            {
                Title = Safe(x.Task.Title),
                Category = Safe(CategoryName(x.Task.CategoryId, categories, strings)),
                Priority = x.Task.Priority.ToString(),
                Deadline = x.Deadline?.ToString("yyyy-MM-dd HH:mm", Inv) ?? "",
                CompletedAt = x.Completed?.ToString("yyyy-MM-dd HH:mm", Inv) ?? "",
                EstimatedMinutes = x.Task.EstimatedMinutes
            })
            .ToList();

        using var memory = new MemoryStream();
        await using (var writer = new StreamWriter(memory, new UTF8Encoding(true), leaveOpen: true))
        await using (var csv = new CsvWriter(writer, Inv))
        {
            await csv.WriteRecordsAsync(rows, ct);
        }

        return memory.ToArray();
    }

    /// <summary>Захист від CSV-ін'єкції: комірка, що починається з керівного символу формули, записується як текст.</summary>
    private static string Safe(string value) =>
        value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;

    private static string CategoryName(Guid? id, Dictionary<Guid, string> names, ReportStrings strings) =>
        id is { } key && names.TryGetValue(key, out var name) ? name : strings.NoCategory;

    private static string PriorityName(TaskPriority priority, ReportStrings s) => priority switch
    {
        TaskPriority.Low => s.Low,
        TaskPriority.High => s.High,
        TaskPriority.Critical => s.Critical,
        _ => s.Medium
    };

    private static DateTime? ToLocal(DateTime? utc, TimeZoneInfo zone) =>
        utc is { } value ? UserTime.ToLocal(value, zone) : null;
}