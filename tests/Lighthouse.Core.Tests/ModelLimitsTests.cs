using System.Diagnostics;
using System.Text.Json;
using Lighthouse.Core.AI;
using Lighthouse.Core.Entities;
using Lighthouse.Shared.Enums;
using Xunit;

namespace Lighthouse.Core.Tests;

/// <summary>Нефункціональні вимоги до AI: стан моделі ≤ 10 КБ, оцінка 200 завдань ≤ 100 мс.</summary>
public class ModelLimitsTests
{
    [Fact]
    public void ModelSnapshot_StaysWithinTenKilobytes()
    {
        var random = new Random(7);
        var model = new LinUcbModel(LinUcbDefaults.Dimension, LinUcbDefaults.Alpha);
        for (var i = 0; i < 500; i++)
        {
            model.Update(Enumerable.Range(0, 10).Select(_ => random.NextDouble()).ToArray(), random.Next(2));
        }

        var json = JsonSerializer.Serialize(model.ToSnapshot());

        Assert.True(json.Length <= 10 * 1024, $"Snapshot is {json.Length} bytes.");
    }

    [Fact]
    public void Rank_TwoHundredTasks_FinishesWithinOneHundredMilliseconds()
    {
        var now = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
        var random = new Random(11);
        var model = new LinUcbModel(LinUcbDefaults.Dimension, LinUcbDefaults.Alpha);
        for (var i = 0; i < 100; i++)
        {
            model.Update(Enumerable.Range(0, 10).Select(_ => random.NextDouble()).ToArray(), random.Next(2));
        }

        var tasks = Enumerable.Range(0, 200).Select(i => new TaskItem
        {
            Priority = (TaskPriority)(i % 4),
            DeadlineUtc = now.AddHours(i - 20),
            EstimatedMinutes = 15 + i % 90,
            ReminderAtUtc = i % 3 == 0 ? now : null
        }).ToList();
        var request = new PrioritizationRequest(tasks, UserHistory.Empty, model, 100, now, TimeZoneInfo.Utc);
        var prioritizer = new Prioritizer();

        prioritizer.Rank(request); // прогрів JIT
        var stopwatch = Stopwatch.StartNew();
        var result = prioritizer.Rank(request);
        stopwatch.Stop();

        Assert.Equal(200, result.Ranked.Count);
        Assert.True(stopwatch.ElapsedMilliseconds <= 100, $"Ranking took {stopwatch.ElapsedMilliseconds} ms.");
    }
}