using Lighthouse.Core.AI;
using Lighthouse.Core.Entities;
using Lighthouse.Shared.Enums;
using Lighthouse.Shared.Ranking;
using Xunit;

namespace Lighthouse.Core.Tests;

public class OfflineRankerTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void OfflineRanking_MatchesServerHeuristic_InOrderScoreLayerAndReasons()
    {
        var entities = new[]
        {
            new TaskItem { Priority = TaskPriority.Low },
            new TaskItem { Priority = TaskPriority.Critical, DeadlineUtc = Now.AddHours(10) },
            new TaskItem { Priority = TaskPriority.High, DeadlineUtc = Now.AddHours(-3) },
            new TaskItem { Priority = TaskPriority.Medium, DeadlineUtc = Now.AddDays(3) },
            new TaskItem { Priority = TaskPriority.High, DeadlineUtc = Now.AddHours(30) },
            new TaskItem { Priority = TaskPriority.Critical }
        };

        var server = new Prioritizer().Rank(
            new PrioritizationRequest(entities, UserHistory.Empty, null, 0, Now, TimeZoneInfo.Utc));
        var offline = OfflineRanker.Rank(entities.Select(e => new OfflineTask(e.Id, e.Priority, e.DeadlineUtc)), Now);

        Assert.Equal(RankingMode.Heuristic, server.Mode);
        Assert.Equal(server.Ranked.Select(r => r.TaskId), offline.Select(r => r.TaskId));
        for (var i = 0; i < offline.Count; i++)
        {
            Assert.Equal(server.Ranked[i].Score, offline[i].Score, precision: 12);
            Assert.Equal(server.Ranked[i].IsLayer1, offline[i].IsLayer1);
            Assert.Equal(server.Ranked[i].Explanation.Reasons.Select(r => r.Code), offline[i].ReasonCodes);
        }
    }

    [Fact]
    public void Overdue_TaskIsFirst_WithOverdueReason()
    {
        var overdue = new OfflineTask(Guid.NewGuid(), TaskPriority.Low, Now.AddHours(-1));
        var critical = new OfflineTask(Guid.NewGuid(), TaskPriority.Critical, null);

        var ranked = OfflineRanker.Rank([critical, overdue], Now);

        Assert.Equal(overdue.Id, ranked[0].TaskId);
        Assert.True(ranked[0].IsLayer1);
        Assert.Contains("overdue", ranked[0].ReasonCodes);
    }
}