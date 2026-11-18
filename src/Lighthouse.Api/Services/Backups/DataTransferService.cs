using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;
using Lighthouse.Api.Data;
using Lighthouse.Api.Infrastructure;
using Lighthouse.Api.Services.Reports;
using Lighthouse.Shared.Categories;
using Lighthouse.Shared.Contracts.Backups;
using Lighthouse.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Lighthouse.Api.Services.Backups;

public interface IDataTransferService
{
    Task<ReportFile> ExportAsync(Guid userId, string format, CancellationToken ct);
    Task<ImportResultDto> ImportAsync(Guid userId, string format, byte[] content, CancellationToken ct);
}

internal sealed class ExportCsvRow
{
    [CsvHelper.Configuration.Attributes.Index(0)] public string Title { get; init; } = "";
    [CsvHelper.Configuration.Attributes.Index(1)] public string Description { get; init; } = "";
    [CsvHelper.Configuration.Attributes.Index(2)] public string Category { get; init; } = "";
    [CsvHelper.Configuration.Attributes.Index(3)] public string Priority { get; init; } = "";
    [CsvHelper.Configuration.Attributes.Index(4)] public string Deadline { get; init; } = "";
    [CsvHelper.Configuration.Attributes.Index(5)] public string CompletedAt { get; init; } = "";
    [CsvHelper.Configuration.Attributes.Index(6)] public int? EstimatedMinutes { get; init; }
}

internal sealed class ImportCsvRow
{
    [Name("title")] public string? Title { get; set; }
    [Name("description")] public string? Description { get; set; }
    [Name("category")] public string? Category { get; set; }
    [Name("priority")] public string? Priority { get; set; }
    [Name("deadline")] public string? Deadline { get; set; }
    [Name("completedat")] public string? CompletedAt { get; set; }
    [Name("estimatedminutes")] public string? EstimatedMinutes { get; set; }
}

/// <summary>Експорт (JSON — повний, CSV — завдання) та імпорт (JSON і CSV) даних користувача.</summary>
public sealed partial class DataTransferService(
    LighthouseDbContext db, BackupSnapshotBuilder snapshots, BackupApplier applier, IUserTimeService userTime)
    : IDataTransferService
{
    private const int MaxCsvRows = 5000;
    private const int MaxReportedErrors = 10;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public async Task<ReportFile> ExportAsync(Guid userId, string format, CancellationToken ct)
    {
        var stamp = DateTime.UtcNow.ToString("yyyy-MM-dd", Inv);
        switch (format.Trim().ToLowerInvariant())
        {
            case "json":
            {
                var document = await snapshots.BuildAsync(userId, DateTime.UtcNow, ct);
                return new ReportFile(BackupFormat.ToJson(document, indented: true), "application/json",
                    $"lighthouse-export-{stamp}.json");
            }
            case "csv":
                return new ReportFile(await BuildCsvAsync(userId, ct), "text/csv", $"lighthouse-export-{stamp}.csv");
            default:
                throw new AppException(StatusCodes.Status400BadRequest, "Format must be json or csv.");
        }
    }

    public async Task<ImportResultDto> ImportAsync(Guid userId, string format, byte[] content, CancellationToken ct)
    {
        if (content.Length == 0)
        {
            throw new AppException(StatusCodes.Status400BadRequest, "The file is empty.");
        }

        return format.Trim().ToLowerInvariant() switch
        {
            "json" => await applier.AddAsync(userId, BackupFormat.ParseJson(content), ct),
            "csv" => await ImportCsvAsync(userId, content, ct),
            _ => throw new AppException(StatusCodes.Status400BadRequest, "Format must be json or csv.")
        };
    }

    private async Task<byte[]> BuildCsvAsync(Guid userId, CancellationToken ct)
    {
        var categories = await db.Categories.AsNoTracking()
            .Where(c => c.UserId == userId && !c.IsDeleted)
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var tasks = await db.Tasks.AsNoTracking()
            .Where(t => t.UserId == userId && !t.IsDeleted).OrderBy(t => t.CreatedAtUtc).ToListAsync(ct);

        static string Iso(DateTime? value) => value?.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", Inv) ?? "";

        var rows = tasks.Select(t => new ExportCsvRow
        {
            Title = CsvText.Safe(t.Title),
            Description = CsvText.Safe(t.Description ?? ""),
            Category = t.CategoryId is { } id && categories.TryGetValue(id, out var name) ? CsvText.Safe(name) : "",
            Priority = t.Priority.ToString(),
            Deadline = Iso(t.DeadlineUtc),
            CompletedAt = Iso(t.CompletedAtUtc),
            EstimatedMinutes = t.EstimatedMinutes
        }).ToList();

        using var memory = new MemoryStream();
        await using (var writer = new StreamWriter(memory, new UTF8Encoding(true), leaveOpen: true))
        await using (var csv = new CsvWriter(writer, Inv))
        {
            await csv.WriteRecordsAsync(rows, ct);
        }

        return memory.ToArray();
    }

    private async Task<ImportResultDto> ImportCsvAsync(Guid userId, byte[] content, CancellationToken ct)
    {
        var zone = await userTime.GetZoneAsync(userId, ct);
        var errors = new List<string>();
        var skipped = 0;

        var categories = new Dictionary<string, BackupCategory>(CategoryNames.Comparer);
        var tasks = new List<BackupTask>();

        var config = new CsvConfiguration(Inv)
        {
            PrepareHeaderForMatch = args => args.Header.Trim().ToLowerInvariant(),
            MissingFieldFound = null,
            HeaderValidated = null,
            BadDataFound = null,
            DetectDelimiter = true,
            DetectDelimiterValues = [",", ";", "\t"]
        };

        using var reader = new StreamReader(new MemoryStream(content), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        using var csv = new CsvReader(reader, config);

        if (!await csv.ReadAsync())
        {
            throw new AppException(StatusCodes.Status400BadRequest, "The file is empty.");
        }

        csv.ReadHeader();
        var rowNumber = 1;
        while (await csv.ReadAsync())
        {
            rowNumber++;
            if (rowNumber - 1 > MaxCsvRows)
            {
                AddError(errors, $"Only the first {MaxCsvRows} rows were imported.");
                break;
            }

            ImportCsvRow row;
            try
            {
                row = csv.GetRecord<ImportCsvRow>()!;
            }
            catch (CsvHelperException)
            {
                skipped++;
                AddError(errors, $"Row {rowNumber}: cannot be read.");
                continue;
            }

            var title = CsvText.Restore((row.Title ?? string.Empty).Trim());
            if (title.Length == 0)
            {
                skipped++;
                AddError(errors, $"Row {rowNumber}: the title is empty.");
                continue;
            }

            Guid? categoryId = null;
            var categoryName = CategoryNames.Normalize(CsvText.Restore(row.Category ?? string.Empty));
            if (categoryName.Length > 0)
            {
                if (!categories.TryGetValue(categoryName, out var category))
                {
                    category = new BackupCategory(Guid.NewGuid(), categoryName, null, null);
                    categories[categoryName] = category;
                }

                categoryId = category.Id;
            }

            var deadline = ParseMoment(row.Deadline, zone);
            var completed = ParseMoment(row.CompletedAt, zone);
            if ((!string.IsNullOrWhiteSpace(row.Deadline) && deadline is null)
                || (!string.IsNullOrWhiteSpace(row.CompletedAt) && completed is null))
            {
                AddError(errors, $"Row {rowNumber}: a date was not recognised and was ignored.");
            }

            tasks.Add(new BackupTask(
                Guid.NewGuid(), title, CsvText.Restore(row.Description ?? string.Empty), deadline, ParsePriority(row.Priority),
                categoryId, null, completed,
                int.TryParse(row.EstimatedMinutes, NumberStyles.Integer, Inv, out var minutes) ? minutes : null, []));
        }

        var document = new BackupDocument(
            BackupDocument.CurrentVersion, DateTime.UtcNow, null, categories.Values.ToList(), tasks, []);
        var result = await applier.AddAsync(userId, document, ct);

        return result with
        {
            Skipped = result.Skipped + skipped,
            Errors = errors.Take(MaxReportedErrors).ToList()
        };
    }

    private static void AddError(List<string> errors, string message)
    {
        if (errors.Count < MaxReportedErrors)
        {
            errors.Add(message);
        }
    }

    private static TaskPriority ParsePriority(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "low" or "0" or "низький" => TaskPriority.Low,
        "high" or "2" or "високий" => TaskPriority.High,
        "critical" or "3" or "критичний" => TaskPriority.Critical,
        _ => TaskPriority.Medium
    };

    /// <summary>Час із явним зсувом (Z, +02:00) береться як є, без зсуву — як час у поясі користувача.</summary>
    private static DateTime? ParseMoment(string? text, TimeZoneInfo zone)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        text = text.Trim();
        if (text.Contains(':') && ExplicitOffset().IsMatch(text))
        {
            return DateTimeOffset.TryParse(text, Inv, DateTimeStyles.None, out var offset) ? offset.UtcDateTime : null;
        }

        if (!DateTime.TryParse(text, Inv, DateTimeStyles.None, out var local))
        {
            return null;
        }

        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        try
        {
            return TimeZoneInfo.ConvertTimeToUtc(local, zone);
        }
        catch (ArgumentException)
        {
            return DateTime.SpecifyKind(local, DateTimeKind.Utc); // неіснуючий через перехід на літній час
        }
    }

    [GeneratedRegex(@"(Z|[+-]\d{2}(:?\d{2})?)$", RegexOptions.IgnoreCase)]
    private static partial Regex ExplicitOffset();
}