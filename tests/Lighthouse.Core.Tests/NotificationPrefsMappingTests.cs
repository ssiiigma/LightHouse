using Lighthouse.Shared.Contracts.Notifications;
using Lighthouse.Shared.Notifications;
using Xunit;

namespace Lighthouse.Core.Tests;

public class NotificationPrefsMappingTests
{
    private static readonly NotificationPrefs Base =
        new(true, true, false, new TimeSpan(21, 30, 0), new TimeSpan(6, 45, 0));

    [Fact]
    public void ToDto_WithQuietDisabled_SendsNoTimes_AndKeepsServerOnlyFlags()
    {
        var dto = NotificationPrefsMapping.ToDto(Base, dailyDigest: true, smartReminders: false);

        Assert.Null(dto.QuietHoursStart);
        Assert.Null(dto.QuietHoursEnd);
        Assert.True(dto.DailyDigestEnabled);
        Assert.False(dto.SmartRemindersEnabled);
    }

    [Fact]
    public void ToDto_WithQuietEnabled_SendsTimes()
    {
        var dto = NotificationPrefsMapping.ToDto(Base with { QuietEnabled = true }, false, true);

        Assert.Equal(new TimeOnly(21, 30), dto.QuietHoursStart);
        Assert.Equal(new TimeOnly(6, 45), dto.QuietHoursEnd);
    }

    [Fact]
    public void Apply_WithoutQuiet_DisablesQuiet_ButKeepsLocalTimes()
    {
        var current = Base with { QuietEnabled = true };

        var applied = NotificationPrefsMapping.Apply(
            new NotificationSettingsDto(false, false, false, true, null, null), current);

        Assert.False(applied.QuietEnabled);
        Assert.Equal(Base.QuietStart, applied.QuietStart);
        Assert.Equal(Base.QuietEnd, applied.QuietEnd);
        Assert.False(applied.TaskReminders);
        Assert.False(applied.HabitReminders);
    }

    [Fact]
    public void Apply_WithQuiet_EnablesItAndTakesTheTimes()
    {
        var applied = NotificationPrefsMapping.Apply(
            new NotificationSettingsDto(true, true, false, true, new TimeOnly(23, 0), new TimeOnly(8, 15)), Base);

        Assert.True(applied.QuietEnabled);
        Assert.Equal(new TimeSpan(23, 0, 0), applied.QuietStart);
        Assert.Equal(new TimeSpan(8, 15, 0), applied.QuietEnd);
    }

    [Fact]
    public void RoundTrip_ReturnsTheSamePrefs()
    {
        var prefs = Base with { QuietEnabled = true, TaskReminders = false };

        var restored = NotificationPrefsMapping.Apply(NotificationPrefsMapping.ToDto(prefs, true, true), Base);

        Assert.Equal(prefs, restored);
    }
}