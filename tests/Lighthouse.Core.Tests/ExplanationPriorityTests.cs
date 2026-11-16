using Lighthouse.Shared.Enums;
using Lighthouse.Shared.Ranking;
using Xunit;

namespace Lighthouse.Core.Tests;

public class ExplanationPriorityTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(TaskPriority.Critical, "priority_critical")]
    [InlineData(TaskPriority.High, "priority_high")]
    [InlineData(TaskPriority.Medium, "priority_medium")]
    public void PriorityReason_MatchesActualPriority(TaskPriority priority, string expectedCode)
    {
        var ranked = OfflineRanker.Rank([new OfflineTask(Guid.NewGuid(), priority, null)], Now);

        Assert.Contains(expectedCode, ranked[0].ReasonCodes);
    }

    [Fact]
    public void LowPriority_HasNoPriorityReason()
    {
        var ranked = OfflineRanker.Rank([new OfflineTask(Guid.NewGuid(), TaskPriority.Low, Now.AddHours(2))], Now);

        Assert.DoesNotContain(ranked[0].ReasonCodes, c => c.StartsWith("priority"));
    }

    [Fact]
    public void ImminentMediumTask_BeatsDistantCritical_ButNotWhenDeadlinesAreEqual()
    {
        var medium = new OfflineTask(Guid.NewGuid(), TaskPriority.Medium, Now.AddHours(1));
        var criticalFar = new OfflineTask(Guid.NewGuid(), TaskPriority.Critical, Now.AddDays(5));
        Assert.Equal(medium.Id, OfflineRanker.Rank([criticalFar, medium], Now)[0].TaskId);

        var high = new OfflineTask(Guid.NewGuid(), TaskPriority.High, Now.AddHours(3));
        var critical = new OfflineTask(Guid.NewGuid(), TaskPriority.Critical, Now.AddHours(3));
        Assert.Equal(critical.Id, OfflineRanker.Rank([high, critical], Now)[0].TaskId);
    }
}