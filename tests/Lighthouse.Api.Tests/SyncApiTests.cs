using System.Net;
using System.Net.Http.Json;
using Lighthouse.Shared.Contracts.Tasks;
using Lighthouse.Shared.Enums;

namespace Lighthouse.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class SyncApiTests(ApiFixture fixture)
{
    private static UpsertTaskRequest Request(string title, DateTime editedAtUtc) =>
        new(title, null, null, TaskPriority.Medium, null, null, null, null, [], editedAtUtc);

    private static async Task<TaskDto> PutAsync(HttpClient client, Guid id, UpsertTaskRequest request)
    {
        var response = await client.PutAsJsonAsync($"/api/tasks/{id}", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TaskDto>())!;
    }

    [SqlServerFact]
    public async Task Upsert_LastWriteWins_StaleEditIsIgnored()
    {
        var user = await fixture.Factory.RegisterAsync();
        var id = Guid.NewGuid();
        var t0 = DateTime.UtcNow.AddMinutes(-30);

        await PutAsync(user.Client, id, Request("v1", t0.AddMinutes(10)));
        var second = await PutAsync(user.Client, id, Request("v2", t0.AddMinutes(20)));
        Assert.Equal("v2", second.Title);

        var stale = await PutAsync(user.Client, id, Request("stale", t0.AddMinutes(5)));

        Assert.Equal("v2", stale.Title); // клієнт отримує актуальний стан сервера
        var all = (await user.Client.GetFromJsonAsync<List<TaskDto>>("/api/tasks"))!;
        Assert.Equal("v2", Assert.Single(all).Title);
    }

    [SqlServerFact]
    public async Task Upsert_ResendingSameRequest_IsIdempotent()
    {
        var user = await fixture.Factory.RegisterAsync();
        var id = Guid.NewGuid();
        var request = Request("Once", DateTime.UtcNow.AddMinutes(-5));

        await PutAsync(user.Client, id, request);
        await PutAsync(user.Client, id, request);

        var all = (await user.Client.GetFromJsonAsync<List<TaskDto>>("/api/tasks"))!;
        Assert.Single(all);
    }

    [SqlServerFact]
    public async Task Delta_ReturnsTombstoneForDeletedTask_WhileFullListHidesIt()
    {
        var user = await fixture.Factory.RegisterAsync();
        var id = Guid.NewGuid();
        var since = DateTime.UtcNow.AddMinutes(-1);

        await PutAsync(user.Client, id, Request("Temp", DateTime.UtcNow.AddMinutes(-5)));
        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.DeleteAsync($"/api/tasks/{id}")).StatusCode);

        var delta = (await user.Client.GetFromJsonAsync<List<TaskDto>>(
            "/api/tasks?modifiedSince=" + Uri.EscapeDataString(since.ToString("O"))))!;
        var tombstone = Assert.Single(delta);
        Assert.True(tombstone.IsDeleted);

        var full = (await user.Client.GetFromJsonAsync<List<TaskDto>>("/api/tasks"))!;
        Assert.Empty(full);
    }
}