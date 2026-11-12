using System.Net;
using System.Net.Http.Json;
using Lighthouse.Shared.Contracts.Categories;
using Lighthouse.Shared.Contracts.Tasks;

namespace Lighthouse.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class OwnershipTests(ApiFixture fixture)
{
    [SqlServerFact]
    public async Task ForeignTask_Returns404_ForUpsertAndDelete_AndRemainsUntouched()
    {
        var owner = await fixture.Factory.RegisterAsync();
        var intruder = await fixture.Factory.RegisterAsync();
        var id = Guid.NewGuid();

        var create = await owner.Client.PutAsJsonAsync($"/api/tasks/{id}", TestApi.NewTask("Owner task"));
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        var upsert = await intruder.Client.PutAsJsonAsync($"/api/tasks/{id}", TestApi.NewTask("Hijacked"));
        Assert.Equal(HttpStatusCode.NotFound, upsert.StatusCode);

        var delete = await intruder.Client.DeleteAsync($"/api/tasks/{id}");
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);

        var ownerTasks = (await owner.Client.GetFromJsonAsync<List<TaskDto>>("/api/tasks"))!;
        var task = Assert.Single(ownerTasks);
        Assert.Equal("Owner task", task.Title);
        Assert.False(task.IsDeleted);

        var intruderTasks = (await intruder.Client.GetFromJsonAsync<List<TaskDto>>("/api/tasks"))!;
        Assert.Empty(intruderTasks);
    }

    [SqlServerFact]
    public async Task ForeignCategory_Returns404_ForUpsertAndDelete()
    {
        var owner = await fixture.Factory.RegisterAsync();
        var intruder = await fixture.Factory.RegisterAsync();
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.OK,
            (await owner.Client.PutAsJsonAsync($"/api/categories/{id}", TestApi.NewCategory("Study"))).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound,
            (await intruder.Client.PutAsJsonAsync($"/api/categories/{id}", TestApi.NewCategory("Hijacked"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await intruder.Client.DeleteAsync($"/api/categories/{id}")).StatusCode);

        var ownerCategories = (await owner.Client.GetFromJsonAsync<List<CategoryDto>>("/api/categories"))!;
        Assert.Equal("Study", Assert.Single(ownerCategories).Name);
    }

    [SqlServerFact]
    public async Task ForeignHabit_Returns404_ForUpsertDeleteAndLogs()
    {
        var owner = await fixture.Factory.RegisterAsync();
        var intruder = await fixture.Factory.RegisterAsync();
        var id = Guid.NewGuid();
        var date = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

        Assert.Equal(HttpStatusCode.OK,
            (await owner.Client.PutAsJsonAsync($"/api/habits/{id}", TestApi.NewHabit("Run"))).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound,
            (await intruder.Client.PutAsJsonAsync($"/api/habits/{id}", TestApi.NewHabit("Hijacked"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await intruder.Client.PutAsync($"/api/habits/{id}/logs/{date}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await intruder.Client.DeleteAsync($"/api/habits/{id}/logs/{date}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await intruder.Client.DeleteAsync($"/api/habits/{id}")).StatusCode);
    }
}