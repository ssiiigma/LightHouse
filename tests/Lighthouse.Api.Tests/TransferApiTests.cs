using System.Net;
using System.Net.Http.Json;
using System.Text;
using Lighthouse.Shared.Contracts.Backups;
using Lighthouse.Shared.Contracts.Categories;
using Lighthouse.Shared.Contracts.Habits;
using Lighthouse.Shared.Contracts.Tasks;
using Lighthouse.Shared.Enums;

namespace Lighthouse.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class TransferApiTests(ApiFixture fixture)
{
    private static async Task<ImportResultDto> ImportAsync(HttpClient client, string format, HttpContent content)
    {
        var response = await client.PostAsync($"/api/data/import?format={format}", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ImportResultDto>())!;
    }

    [SqlServerFact]
    public async Task JsonExport_ImportedIntoAnotherAccount_AddsNewRecords_AndMergesCategoriesByName()
    {
        var owner = await fixture.Factory.RegisterAsync();
        var other = await fixture.Factory.RegisterAsync();

        var category = Guid.NewGuid();
        var habit = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        await owner.Client.PutAsJsonAsync($"/api/categories/{category}", TestApi.NewCategory("Study"));
        await owner.Client.PutAsJsonAsync($"/api/tasks/{Guid.NewGuid()}",
            new UpsertTaskRequest("With category", null, null, TaskPriority.High, category, null, null, 30, [], DateTime.UtcNow));
        await owner.Client.PutAsJsonAsync($"/api/habits/{habit}", TestApi.NewHabit("Run"));
        await owner.Client.PutAsync($"/api/habits/{habit}/logs/{today}", null);

        var otherCategory = Guid.NewGuid();
        await other.Client.PutAsJsonAsync($"/api/categories/{otherCategory}", TestApi.NewCategory("study"));

        var export = await owner.Client.GetAsync("/api/data/export?format=json");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        var bytes = await export.Content.ReadAsByteArrayAsync();

        var result = await ImportAsync(other.Client, "json", new ByteArrayContent(bytes));

        Assert.Equal(0, result.Categories);
        Assert.Equal(1, result.Tasks);
        Assert.Equal(1, result.Habits);
        var tasks = (await other.Client.GetFromJsonAsync<List<TaskDto>>("/api/tasks"))!;
        Assert.Equal(otherCategory, Assert.Single(tasks).CategoryId);
        var habits = (await other.Client.GetFromJsonAsync<List<HabitDto>>("/api/habits"))!;
        Assert.Single(Assert.Single(habits).Logs);
        Assert.Single((await other.Client.GetFromJsonAsync<List<CategoryDto>>("/api/categories"))!);
        Assert.Single((await owner.Client.GetFromJsonAsync<List<TaskDto>>("/api/tasks"))!);
    }

    [SqlServerFact]
    public async Task CsvImport_ParsesRows_AppliesDefaults_AndReportsSkippedRows()
    {
        var user = await fixture.Factory.RegisterAsync();
        const string csv =
            "Title,Description,Category,Priority,Deadline,CompletedAt,EstimatedMinutes\n" +
            "Buy milk,2%,Errands,High,2026-12-01T10:00:00Z,,15\n" +
            ",no title,,,,,\n" +
            "Bad priority,,,Urgent!,,,\n";

        var result = await ImportAsync(user.Client, "csv", new StringContent(csv, Encoding.UTF8, "text/csv"));

        Assert.Equal(2, result.Tasks);
        Assert.Equal(1, result.Categories);
        Assert.Equal(1, result.Skipped);
        Assert.NotEmpty(result.Errors);

        var tasks = (await user.Client.GetFromJsonAsync<List<TaskDto>>("/api/tasks"))!;
        var milk = tasks.Single(t => t.Title == "Buy milk");
        Assert.Equal(TaskPriority.High, milk.Priority);
        Assert.Equal(new DateTime(2026, 12, 1, 10, 0, 0, DateTimeKind.Utc), milk.DeadlineUtc!.Value.ToUniversalTime());
        Assert.Equal(15, milk.EstimatedMinutes);
        Assert.Equal(TaskPriority.Medium, tasks.Single(t => t.Title == "Bad priority").Priority);
        Assert.Equal("Errands", Assert.Single((await user.Client.GetFromJsonAsync<List<CategoryDto>>("/api/categories"))!).Name);
    }

    [SqlServerFact]
    public async Task CsvExport_RoundTrips_AndNeutralisesFormulas()
    {
        var owner = await fixture.Factory.RegisterAsync();
        var other = await fixture.Factory.RegisterAsync();
        await owner.Client.PutAsJsonAsync($"/api/tasks/{Guid.NewGuid()}", TestApi.NewTask("=SUM(1)"));

        var text = await owner.Client.GetStringAsync("/api/data/export?format=csv");
        Assert.Contains("'=SUM(1)", text);

        await ImportAsync(other.Client, "csv", new StringContent(text, Encoding.UTF8, "text/csv"));
        var tasks = (await other.Client.GetFromJsonAsync<List<TaskDto>>("/api/tasks"))!;
        Assert.Equal("=SUM(1)", Assert.Single(tasks).Title);
    }

    [SqlServerFact]
    public async Task Import_RejectsEmptyGarbageAndUnknownFormats()
    {
        var user = await fixture.Factory.RegisterAsync();

        Assert.Equal(HttpStatusCode.BadRequest,
            (await user.Client.PostAsync("/api/data/import?format=csv", new ByteArrayContent([]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await user.Client.PostAsync("/api/data/import?format=json", new StringContent("garbage"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await user.Client.PostAsync("/api/data/import?format=xml", new StringContent("<a/>"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await user.Client.GetAsync("/api/data/export?format=xml")).StatusCode);
    }

    [SqlServerFact]
    public async Task Transfer_RequiresAuthentication()
    {
        var anonymous = fixture.Factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/data/export?format=json")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsync("/api/data/import?format=json", new StringContent("{}"))).StatusCode);
    }
}