using Lighthouse.Api.Infrastructure;
using Lighthouse.Api.Services.Backups;
using Lighthouse.Shared.Enums;

namespace Lighthouse.Api.Tests;

public class BackupFormatTests
{
    private static BackupDocument Sample() => new(
        BackupDocument.CurrentVersion, new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc), "Europe/Kyiv",
        [new BackupCategory(Guid.NewGuid(), "Навчання", "#4C8BF5", null)],
        [new BackupTask(Guid.NewGuid(), "Звіт", "опис", null, TaskPriority.High, null, null, null, 30,
            [new BackupSubtask(Guid.NewGuid(), "Чернетка", true, 0)])],
        [new BackupHabit(Guid.NewGuid(), "Біг", null, HabitFrequencyType.Daily, 0, null, new TimeOnly(7, 30), null, null, null,
            [new BackupHabitLog(new DateOnly(2026, 11, 9), new DateTime(2026, 11, 9, 6, 0, 0, DateTimeKind.Utc))])]);

    [Fact]
    public void Document_RoundTripsThroughGzipJson()
    {
        var original = Sample();

        var restored = BackupFormat.ParseJson(BackupFormat.Gunzip(BackupFormat.Gzip(BackupFormat.ToJson(original))));

        Assert.Equal(original.Categories, restored.Categories);
        Assert.Equal(original.Tasks[0].Title, restored.Tasks[0].Title);
        Assert.Equal(TaskPriority.High, restored.Tasks[0].Priority);
        Assert.Equal(original.Habits[0].ReminderTime, restored.Habits[0].ReminderTime);
        Assert.Equal(original.Habits[0].Logs[0].Date, restored.Habits[0].Logs[0].Date);
    }

    [Fact]
    public void ParseJson_HandlesBomAndRejectsGarbageAndWrongVersion()
    {
        var withBom = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(BackupFormat.ToJson(Sample())).ToArray();
        Assert.Single(BackupFormat.ParseJson(withBom).Tasks);

        Assert.Equal(400, Assert.Throws<AppException>(() => BackupFormat.ParseJson("not json"u8.ToArray())).StatusCode);
        Assert.Equal(400, Assert.Throws<AppException>(() => BackupFormat.ParseJson(
            BackupFormat.ToJson(Sample() with { Version = 99 }))).StatusCode);
    }

    [Fact]
    public void Gunzip_RefusesOversizedAndCorruptedData()
    {
        var bomb = BackupFormat.Gzip(new byte[1_000_000]);

        Assert.Equal(400, Assert.Throws<AppException>(() => BackupFormat.Gunzip(bomb, maxBytes: 1000)).StatusCode);
        Assert.Equal(400, Assert.Throws<AppException>(() => BackupFormat.Gunzip([1, 2, 3, 4])).StatusCode);
    }

    [Fact]
    public void Json_UsesEnumNames()
    {
        var text = System.Text.Encoding.UTF8.GetString(BackupFormat.ToJson(Sample()));

        Assert.Contains("\"priority\":\"High\"", text);
    }
}