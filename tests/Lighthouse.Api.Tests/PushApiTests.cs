using System.Net;
using System.Net.Http.Json;
using Lighthouse.Api.Data;
using Lighthouse.Api.Services.Push;
using Lighthouse.Shared.Contracts.Notifications;
using Lighthouse.Shared.Contracts.Tasks;
using Lighthouse.Shared.Enums;
using Lighthouse.Shared.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lighthouse.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class PushApiTests(ApiFixture fixture)
{
    private FakePushSender Push => fixture.Factory.Push;

    private static string NewToken() => "fcm-" + Guid.NewGuid().ToString("N");

    private async Task<int> RunSchedulerAsync(DateTime nowUtc)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<PushScheduler>().RunAsync(nowUtc, CancellationToken.None);
    }

    private static DateTime TodayAt(int hour, int minute) => DateTime.UtcNow.Date.AddHours(hour).AddMinutes(minute);

    private static async Task<Session> PrepareAsync(ApiFactory factory, string token, string title, bool digest = true)
    {
        var user = await factory.RegisterAsync();
        await user.Client.PostAsJsonAsync("/api/devices", new RegisterDeviceRequest(token));
        await user.Client.PutAsJsonAsync("/api/notification-settings",
            new NotificationSettingsDto(true, true, digest, false, null, null));
        await user.Client.PutAsJsonAsync($"/api/tasks/{Guid.NewGuid()}",
            new UpsertTaskRequest(title, null, DateTime.UtcNow.AddDays(-1), TaskPriority.High, null, null, null, null, [], DateTime.UtcNow));
        return user;
    }

    [SqlServerFact]
    public async Task Digest_IsSentOnce_AtTheDigestHour()
    {
        var token = NewToken();
        var title = "Digest-" + Guid.NewGuid().ToString("N");
        await PrepareAsync(fixture.Factory, token, title);

        await RunSchedulerAsync(TodayAt(8, 30));
        await RunSchedulerAsync(TodayAt(8, 45)); // повторно того ж дня нічого не надсилається

        var sent = Push.Sent.Where(s => s.Token == token).ToList();
        var push = Assert.Single(sent);
        Assert.Equal("digest", push.Data["kind"]);
        Assert.Equal("1", push.Data["count"]);
        Assert.Equal(title, push.Data["task"]);
    }

    [SqlServerFact]
    public async Task Nothing_IsSent_WhenDisabledOrOutsideTheWindow()
    {
        var disabledToken = NewToken();
        var lateToken = NewToken();
        await PrepareAsync(fixture.Factory, disabledToken, "Disabled-" + Guid.NewGuid().ToString("N"), digest: false);
        await PrepareAsync(fixture.Factory, lateToken, "Late-" + Guid.NewGuid().ToString("N"));

        await RunSchedulerAsync(TodayAt(8, 30));
        await RunSchedulerAsync(TodayAt(15, 0));

        Assert.DoesNotContain(Push.Sent, s => s.Token == disabledToken);
        Assert.Single(Push.Sent.Where(s => s.Token == lateToken)); // лише вранці
    }

    [SqlServerFact]
    public async Task UnregisteredTokens_AreForgotten()
    {
        var token = NewToken();
        var user = await PrepareAsync(fixture.Factory, token, "Gone-" + Guid.NewGuid().ToString("N"));
        Push.UnregisteredTokens[token] = true;

        await RunSchedulerAsync(TodayAt(8, 30));

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LighthouseDbContext>();
        Assert.False(await db.DeviceTokens.AnyAsync(d => d.FcmToken == token));
        Assert.DoesNotContain(Push.Sent, s => s.Token == token);
    }

    [SqlServerFact]
    public async Task TestPush_ReachesTheUsersDevices()
    {
        var user = await fixture.Factory.RegisterAsync();
        var token = NewToken();
        await user.Client.PostAsJsonAsync("/api/devices", new RegisterDeviceRequest(token));

        var response = await user.Client.PostAsync("/api/devices/test", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, (await response.Content.ReadFromJsonAsync<NotificationPlanner.PushTestResultDto>())!.Sent);
        Assert.Equal("test", Push.Sent.Single(s => s.Token == token).Data["kind"]);

        var empty = await fixture.Factory.RegisterAsync();
        Assert.Equal(0, (await (await empty.Client.PostAsync("/api/devices/test", null)).Content
            .ReadFromJsonAsync<NotificationPlanner.PushTestResultDto>())!.Sent);
    }

    [SqlServerFact]
    public async Task TestPush_RequiresAuthentication()
    {
        var anonymous = fixture.Factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/devices/test", null)).StatusCode);
    }
}