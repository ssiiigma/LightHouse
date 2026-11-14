using Lighthouse.Shared.Sync;
using Xunit;

namespace Lighthouse.Core.Tests;

public class SyncRulesTests
{
    private static readonly DateTime T = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NoPendingLocalChange_AlwaysAppliesRemote()
    {
        Assert.True(SyncRules.ShouldApplyRemote(null, T));
        Assert.True(SyncRules.ShouldApplyRemote(null, DateTime.MinValue));
    }

    [Fact]
    public void NewerRemote_WinsOverPendingLocal()
    {
        Assert.True(SyncRules.ShouldApplyRemote(T, T.AddSeconds(1)));
    }

    [Fact]
    public void OlderRemote_LosesToPendingLocal()
    {
        Assert.False(SyncRules.ShouldApplyRemote(T, T.AddSeconds(-1)));
    }

    [Fact]
    public void EqualTimestamps_KeepLocal_MatchingServerBehavior()
    {
        // Сервер ігнорує запис клієнта лише якщо власна правка суворо новіша, тож за рівних міток перемагає клієнт.
        Assert.False(SyncRules.ShouldApplyRemote(T, T));
    }
}