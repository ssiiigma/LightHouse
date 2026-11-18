using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lighthouse.Api.Infrastructure;
using Lighthouse.Shared.Enums;

namespace Lighthouse.Api.Services.Backups;

public sealed record BackupCategory(Guid Id, string Name, string? ColorHex, string? Icon);

public sealed record BackupSubtask(Guid Id, string Title, bool IsDone, int SortOrder);

public sealed record BackupTask(
    Guid Id, string Title, string? Description, DateTime? DeadlineUtc, TaskPriority Priority, Guid? CategoryId,
    DateTime? ReminderAtUtc, DateTime? CompletedAtUtc, int? EstimatedMinutes, List<BackupSubtask> Subtasks);

public sealed record BackupHabitLog(DateOnly Date, DateTime CompletedAtUtc);

public sealed record BackupHabit(
    Guid Id, string Title, string? Description, HabitFrequencyType FrequencyType, int DaysOfWeekMask, int? TimesPerWeek,
    TimeOnly? ReminderTime, string? ColorHex, string? Icon, Guid? CategoryId, List<BackupHabitLog> Logs);

/// <summary>Формат файлу копії й експорту JSON (версія 1).</summary>
public sealed record BackupDocument(
    int Version, DateTime CreatedAtUtc, string? TimeZoneId,
    List<BackupCategory> Categories, List<BackupTask> Tasks, List<BackupHabit> Habits)
{
    public const int CurrentVersion = 1;
}

public static class BackupFormat
{
    public const int MaxItems = 20_000;
    public const int MaxUncompressedBytes = 20 * 1024 * 1024;

    private static readonly JsonSerializerOptions Compact = Create(false);
    private static readonly JsonSerializerOptions Indented = Create(true);

    private static JsonSerializerOptions Create(bool indented) => new(JsonSerializerDefaults.Web)
    {
        WriteIndented = indented,
        Converters = { new JsonStringEnumConverter() }
    };

    public static byte[] ToJson(BackupDocument document, bool indented = false) =>
        JsonSerializer.SerializeToUtf8Bytes(document, indented ? Indented : Compact);

    public static BackupDocument ParseJson(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            bytes = bytes[3..]; // BOM
        }

        BackupDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<BackupDocument>(bytes, Compact);
        }
        catch (JsonException)
        {
            throw new AppException(StatusCodes.Status400BadRequest, "The file is not a valid Lighthouse export.");
        }

        if (document is null || document.Version != BackupDocument.CurrentVersion)
        {
            throw new AppException(StatusCodes.Status400BadRequest, "Unsupported file format or version.");
        }

        return document with
        {
            Categories = document.Categories ?? [],
            Tasks = document.Tasks ?? [],
            Habits = document.Habits ?? []
        };
    }

    public static byte[] Gzip(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(data, 0, data.Length);
        }

        return output.ToArray();
    }

    /// <summary>Розпакування з обмеженням розміру (захист від «бомби»).</summary>
    public static byte[] Gunzip(byte[] data, int maxBytes = MaxUncompressedBytes)
    {
        try
        {
            using var input = new MemoryStream(data);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (output.Length + read > maxBytes)
                {
                    throw new AppException(StatusCodes.Status400BadRequest, "The backup is too large.");
                }

                output.Write(buffer, 0, read);
            }

            return output.ToArray();
        }
        catch (InvalidDataException)
        {
            throw new AppException(StatusCodes.Status400BadRequest, "The backup is corrupted.");
        }
    }
}