using System.Net;
using System.Net.Http.Json;
using Lighthouse.Api.Data;
using Lighthouse.Shared.Contracts.Notifications;
using Lighthouse.Shared.Contracts.Profile;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lighthouse.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class ProfileSettingsApiTests(ApiFixture fixture)
{
    private async Task<T> WithDbAsync<T>(Func<LighthouseDbContext, Task<T>> action)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<LighthouseDbContext>());
    }

    [SqlServerFact]
    public async Task Profile_ReturnsData_AndAllowsRenaming()
    {
        var user = await fixture.Factory.RegisterAsync();

        var profile = (await user.Client.GetFromJsonAsync<ProfileDto>("/api/profile"))!;
        Assert.Equal("Test User", profile.Name);
        Assert.Equal(user.Email, profile.Email);
        Assert.False(profile.IsEmailConfirmed);

        var response = await user.Client.PutAsJsonAsync("/api/profile", new UpdateProfileRequest("  New Name "));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("New Name", (await response.Content.ReadFromJsonAsync<ProfileDto>())!.Name);
        Assert.Equal("New Name", (await user.Client.GetFromJsonAsync<ProfileDto>("/api/profile"))!.Name);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await user.Client.PutAsJsonAsync("/api/profile", new UpdateProfileRequest("   "))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await user.Client.PutAsJsonAsync("/api/profile", new UpdateProfileRequest(new string('x', 101)))).StatusCode);
    }

    [SqlServerFact]
    public async Task NotificationSettings_HaveDefaults_AndRoundTrip()
    {
        var user = await fixture.Factory.RegisterAsync();

        var defaults = (await user.Client.GetFromJsonAsync<NotificationSettingsDto>("/api/notification-settings"))!;
        Assert.True(defaults.TaskRemindersEnabled);
        Assert.True(defaults.HabitRemindersEnabled);
        Assert.False(defaults.DailyDigestEnabled);
        Assert.True(defaults.SmartRemindersEnabled);
        Assert.Null(defaults.QuietHoursStart);

        var changed = new NotificationSettingsDto(false, true, true, false, new TimeOnly(22, 0), new TimeOnly(7, 30));
        var put = await user.Client.PutAsJsonAsync("/api/notification-settings", changed);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal(changed, await put.Content.ReadFromJsonAsync<NotificationSettingsDto>());
        Assert.Equal(changed, await user.Client.GetFromJsonAsync<NotificationSettingsDto>("/api/notification-settings"));

        var cleared = changed with { QuietHoursStart = null, QuietHoursEnd = null };
        await user.Client.PutAsJsonAsync("/api/notification-settings", cleared);
        Assert.Equal(cleared, await user.Client.GetFromJsonAsync<NotificationSettingsDto>("/api/notification-settings"));
    }

    [SqlServerFact]
    public async Task NotificationSettings_RejectHalfDefinedQuietHours_AndAreIsolatedPerUser()
    {
        var first = await fixture.Factory.RegisterAsync();
        var second = await fixture.Factory.RegisterAsync();

        var half = new NotificationSettingsDto(true, true, false, true, new TimeOnly(22, 0), null);
        Assert.Equal(HttpStatusCode.BadRequest, (await first.Client.PutAsJsonAsync("/api/notification-settings", half)).StatusCode);

        await first.Client.PutAsJsonAsync("/api/notification-settings",
            new NotificationSettingsDto(false, false, true, false, null, null));
        var other = (await second.Client.GetFromJsonAsync<NotificationSettingsDto>("/api/notification-settings"))!;
        Assert.True(other.TaskRemindersEnabled);
    }

    [SqlServerFact]
    public async Task Devices_RegisterIdempotently_MoveBetweenAccounts_AndUnregisterOnlyForTheOwner()
    {
        var first = await fixture.Factory.RegisterAsync();
        var second = await fixture.Factory.RegisterAsync();
        var token = "fcm-" + Guid.NewGuid().ToString("N");

        Assert.Equal(HttpStatusCode.NoContent, (await first.Client.PostAsJsonAsync("/api/devices", new RegisterDeviceRequest(token))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await first.Client.PostAsJsonAsync("/api/devices", new RegisterDeviceRequest(token))).StatusCode);
        Assert.Equal(1, await WithDbAsync(db => db.DeviceTokens.CountAsync(d => d.FcmToken == token)));

        await second.Client.PostAsJsonAsync("/api/devices", new RegisterDeviceRequest(token));
        Assert.Equal(second.Auth.User.Id, await WithDbAsync(db => db.DeviceTokens.Where(d => d.FcmToken == token).Select(d => d.UserId).SingleAsync()));

        await first.Client.DeleteAsync($"/api/devices?token={token}"); // не власник
        Assert.Equal(1, await WithDbAsync(db => db.DeviceTokens.CountAsync(d => d.FcmToken == token)));

        Assert.Equal(HttpStatusCode.NoContent, (await second.Client.DeleteAsync($"/api/devices?token={token}")).StatusCode);
        Assert.Equal(0, await WithDbAsync(db => db.DeviceTokens.CountAsync(d => d.FcmToken == token)));
    }

    [SqlServerFact]
    public async Task Devices_AreLimitedToTenPerUser_AndValidated()
    {
        var user = await fixture.Factory.RegisterAsync();
        for (var i = 0; i < 12; i++)
        {
            await user.Client.PostAsJsonAsync("/api/devices", new RegisterDeviceRequest($"fcm-{Guid.NewGuid():N}-{i}"));
        }

        Assert.Equal(10, await WithDbAsync(db => db.DeviceTokens.CountAsync(d => d.UserId == user.Auth.User.Id)));
        Assert.Equal(HttpStatusCode.BadRequest,
            (await user.Client.PostAsJsonAsync("/api/devices", new RegisterDeviceRequest(""))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await user.Client.PostAsJsonAsync("/api/devices", new RegisterDeviceRequest(new string('t', 513)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await user.Client.DeleteAsync("/api/devices")).StatusCode);
    }

    [SqlServerFact]
    public async Task ProfileSettingsAndDevices_RequireAuthentication()
    {
        var anonymous = fixture.Factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/notification-settings")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync("/api/devices", new RegisterDeviceRequest("x"))).StatusCode);
    }
}