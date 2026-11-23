using Lighthouse.Api.Services.Calendar;
using Microsoft.AspNetCore.DataProtection;

namespace Lighthouse.Api.Tests;

public class CalendarStateProtectorTests
{
    private static CalendarStateProtector Create() => new(new EphemeralDataProtectionProvider());

    [Fact]
    public void State_RoundTripsTheUserId()
    {
        var protector = Create();
        var userId = Guid.NewGuid();

        Assert.Equal(userId, protector.Read(protector.Create(userId)));
    }

    [Fact]
    public void TamperedOrForeignOrExpiredState_IsRejected()
    {
        var protector = Create();
        var state = protector.Create(Guid.NewGuid());

        Assert.Null(protector.Read(state[..^4] + "AAAA"));
        Assert.Null(protector.Read("garbage"));
        Assert.Null(Create().Read(state)); // інший ключ шифрування
        Assert.Null(protector.Read(protector.Create(Guid.NewGuid(), TimeSpan.FromSeconds(-1))));
    }

    [Fact]
    public void State_DoesNotRevealTheUserId()
    {
        var userId = Guid.NewGuid();

        Assert.DoesNotContain(userId.ToString("N"), Create().Create(userId));
    }
}