using System.Net;
using System.Net.Http.Json;
using System.Web;
using Lighthouse.Shared.Contracts.Calendar;
using Lighthouse.Shared.Enums;
using Lighthouse.Shared.Contracts.Tasks;

namespace Lighthouse.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class CalendarApiTests(ApiFixture fixture)
{
    private static async Task<CalendarStatusDto> StatusAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<CalendarStatusDto>("/api/calendar/status"))!;
    
    private static async Task<string> ConnectStateAsync(HttpClient client)
    {
        var response = await client.PostAsync("/api/calendar/connect", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var url = (await response.Content.ReadFromJsonAsync<CalendarConnectDto>())!.AuthorizationUrl;
        return HttpUtility.ParseQueryString(new Uri(url).Query)["state"]!;
    }

        private static async Task PutTaskAsync(HttpClient client, Guid id, string title, DateTime deadline, DateTime? completed)
    {
        await Task.Delay(30); // clientUpdatedAtUtc має зростати
        var response = await client.PutAsJsonAsync($"/api/tasks/{id}",
            new UpsertTaskRequest(title, null, deadline, TaskPriority.High, null, null, completed, 45, [], DateTime.UtcNow));
        Assert.True(response.StatusCode == HttpStatusCode.OK, "PUT task: " + await response.Content.ReadAsStringAsync());
    }

    private static async Task SyncAsync(HttpClient client)
    {
        var response = await client.PostAsync("/api/calendar/sync", null);
        Assert.True(response.StatusCode == HttpStatusCode.OK, "POST sync: " + await response.Content.ReadAsStringAsync());
    }

    [SqlServerFact]
    public async Task Connect_Callback_Sync_Update_Complete_Disconnect_WorkEndToEnd()
    {
        var user = await fixture.Factory.RegisterAsync();
        var calendar = fixture.Factory.Calendar;
        var title = "Cal-" + Guid.NewGuid().ToString("N");
        var taskId = Guid.NewGuid();
        var deadline = DateTime.UtcNow.AddDays(2);

        Assert.False((await StatusAsync(user.Client)).Connected);
        await PutTaskAsync(user.Client, taskId, title, deadline, completed: null);

        var state = await ConnectStateAsync(user.Client);
        var callback = await fixture.Factory.CreateClient()
            .GetAsync($"/api/calendar/callback?code=good&state={Uri.EscapeDataString(state)}");
        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
        Assert.True((await StatusAsync(user.Client)).Connected);

        // Явна синхронізація: блокування користувача дочекається фонового запуску після повернення з Google.
        await SyncAsync(user.Client);
        var created = calendar.Events.Values.SingleOrDefault(e => e.Summary == title);
        Assert.NotNull(created);
        Assert.True(Math.Abs((created!.EndUtc - deadline).TotalSeconds) < 1);
        Assert.Equal(TimeSpan.FromMinutes(45), created.EndUtc - created.StartUtc);
        Assert.Equal(1, (await StatusAsync(user.Client)).EventCount);

        await PutTaskAsync(user.Client, taskId, title + "-v2", deadline, completed: null);
        await SyncAsync(user.Client);
        Assert.Contains(calendar.Events.Values, e => e.Summary == title + "-v2");

        await PutTaskAsync(user.Client, taskId, title + "-v2", deadline, completed: DateTime.UtcNow);
        await SyncAsync(user.Client);
        Assert.DoesNotContain(calendar.Events.Values, e => e.Summary.StartsWith(title));

        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.DeleteAsync("/api/calendar")).StatusCode);
        Assert.False((await StatusAsync(user.Client)).Connected);
        Assert.Contains("revoke", calendar.Operations);
    }

    [SqlServerFact]
    public async Task Callback_RejectsInvalidStateErrorsAndBadCodes()
    {
        var user = await fixture.Factory.RegisterAsync();
        var anonymous = fixture.Factory.CreateClient();
        var state = Uri.EscapeDataString(await ConnectStateAsync(user.Client));

        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.GetAsync("/api/calendar/callback?code=good&state=garbage")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.GetAsync("/api/calendar/callback")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await anonymous.GetAsync($"/api/calendar/callback?error=access_denied&state={state}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await anonymous.GetAsync($"/api/calendar/callback?code=bad&state={state}")).StatusCode);
        Assert.False((await StatusAsync(user.Client)).Connected);
    }

    [SqlServerFact]
    public async Task Sync_WithoutConnection_Returns409_AndDisconnectIsIdempotent()
    {
        var user = await fixture.Factory.RegisterAsync();

        Assert.Equal(HttpStatusCode.Conflict, (await user.Client.PostAsync("/api/calendar/sync", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.DeleteAsync("/api/calendar")).StatusCode);
    }

    [SqlServerFact]
    public async Task Connection_IsIsolatedPerUser()
    {
        var first = await fixture.Factory.RegisterAsync();
        var second = await fixture.Factory.RegisterAsync();
        var state = await ConnectStateAsync(first.Client);
        await fixture.Factory.CreateClient().GetAsync($"/api/calendar/callback?code=good&state={Uri.EscapeDataString(state)}");

        Assert.True((await StatusAsync(first.Client)).Connected);
        Assert.False((await StatusAsync(second.Client)).Connected);

        await first.Client.DeleteAsync("/api/calendar");
    }

    [SqlServerFact]
    public async Task Calendar_RequiresAuthentication_ExceptTheCallback()
    {
        var anonymous = fixture.Factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/calendar/status")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/calendar/connect", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/calendar/sync", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync("/api/calendar")).StatusCode);
    }
    
    [SqlServerFact]
    public async Task Sync_PersistsLinksForManyTasksAndUsers()
    {
        var first = await fixture.Factory.RegisterAsync();
        var second = await fixture.Factory.RegisterAsync();
        var tag = Guid.NewGuid().ToString("N");

        foreach (var (user, count) in new[] { (first, 3), (second, 2) })
        {
            for (var i = 0; i < count; i++)
            {
                await PutTaskAsync(user.Client, Guid.NewGuid(), $"Many-{tag}-{i}", DateTime.UtcNow.AddDays(1 + i), completed: null);
            }

            var state = await ConnectStateAsync(user.Client);
            await fixture.Factory.CreateClient()
                .GetAsync($"/api/calendar/callback?code=good&state={Uri.EscapeDataString(state)}");
            await SyncAsync(user.Client);
        }

        Assert.Equal(3, (await StatusAsync(first.Client)).EventCount);
        Assert.Equal(2, (await StatusAsync(second.Client)).EventCount);

        await SyncAsync(first.Client); // повторна синхронізація нічого не дублює
        Assert.Equal(5, fixture.Factory.Calendar.Events.Values.Count(e => e.Summary.StartsWith($"Many-{tag}")));

        await first.Client.DeleteAsync("/api/calendar");
        await second.Client.DeleteAsync("/api/calendar");
    }
}