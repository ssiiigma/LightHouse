using Lighthouse.Api.Services.Calendar;
using Microsoft.AspNetCore.DataProtection;

namespace Lighthouse.Api.Tests;

public sealed class CalendarTokenProtectorTests
{
    private const string Token = "1//0g-google-refresh-token-example";

    private static CalendarTokenProtector Create() => new(new EphemeralDataProtectionProvider());

    [Fact]
    public void Protect_ProducesCiphertext_ThatRoundTrips()
    {
        var protector = Create();

        var protectedValue = protector.Protect(Token);

        Assert.DoesNotContain(Token, protectedValue);
        Assert.Equal(Token, protector.Unprotect(protectedValue));
    }

    [Fact]
    public void Unprotect_TamperedValue_Throws()
    {
        var protector = Create();
        var value = protector.Protect(Token);
        var middle = value.Length / 2;
        var tampered = value[..middle] + (value[middle] == 'A' ? 'B' : 'A') + value[(middle + 1)..];

        Assert.Throws<InvalidOperationException>(() => protector.Unprotect(tampered));
    }

    [Fact]
    public void Unprotect_WithDifferentKeyRing_Throws()
    {
        var value = Create().Protect(Token);

        Assert.Throws<InvalidOperationException>(() => Create().Unprotect(value));
    }
}