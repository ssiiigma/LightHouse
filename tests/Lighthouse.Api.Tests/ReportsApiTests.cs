using System.Net;
using System.Net.Http.Json;
using System.Text;
using Lighthouse.Shared.Contracts.Tasks;
using Lighthouse.Shared.Enums;

namespace Lighthouse.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class ReportsApiTests(ApiFixture fixture)
{
    private static string Today => DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
    private static string WeekAgo => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7)).ToString("yyyy-MM-dd");

    private static async Task CreateCompletedTaskAsync(HttpClient client, string title)
    {
        var done = DateTime.UtcNow.AddMinutes(-5);
        var request = new UpsertTaskRequest(title, null, null, TaskPriority.High, null, null, done, 45, [], done);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/tasks/{Guid.NewGuid()}", request)).StatusCode);
    }

    [SqlServerFact]
    public async Task Csv_ContainsOnlyOwnTasksInRange_WithUtf8Bom()
    {
        var owner = await fixture.Factory.RegisterAsync();
        var other = await fixture.Factory.RegisterAsync();
        await CreateCompletedTaskAsync(owner.Client, "Звітне завдання");
        await CreateCompletedTaskAsync(other.Client, "Чуже завдання");

        var response = await owner.Client.GetAsync($"/api/reports?format=csv&from={WeekAgo}&to={Today}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
        var text = Encoding.UTF8.GetString(bytes);
        Assert.StartsWith("\uFEFFTitle,Category,Priority,Deadline,CompletedAt,EstimatedMinutes", text);
        Assert.Contains("Звітне завдання", text);
        Assert.DoesNotContain("Чуже завдання", text);
    }

    [SqlServerFact]
    public async Task Csv_EscapesFormulaCharacters()
    {
        var user = await fixture.Factory.RegisterAsync();
        await CreateCompletedTaskAsync(user.Client, "=HYPERLINK(\"http://x\")");

        var text = await user.Client.GetStringAsync($"/api/reports?format=csv&from={WeekAgo}&to={Today}");

        Assert.DoesNotContain(",=HYPERLINK", text);
        Assert.Contains("'=HYPERLINK", text);
    }

    [SqlServerFact]
    public async Task Pdf_IsGeneratedForBothLanguages()
    {
        var user = await fixture.Factory.RegisterAsync();
        await CreateCompletedTaskAsync(user.Client, "PDF task");

        foreach (var lang in new[] { "uk", "en" })
        {
            var response = await user.Client.GetAsync($"/api/reports?format=pdf&from={WeekAgo}&to={Today}&lang={lang}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
            var bytes = await response.Content.ReadAsByteArrayAsync();
            Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
            Assert.True(bytes.Length > 1000);
        }
    }

    [SqlServerFact]
    public async Task InvalidRequests_Return400()
    {
        var user = await fixture.Factory.RegisterAsync();

        foreach (var url in new[]
                 {
                     $"/api/reports?format=docx&from={WeekAgo}&to={Today}",
                     $"/api/reports?format=csv&from={Today}&to={WeekAgo}",
                     "/api/reports?format=csv&from=2020-01-01&to=2026-01-01",
                     "/api/reports?format=csv&from=bad&to=2026-01-01",
                     "/api/reports?format=csv"
                 })
        {
            var status = (await user.Client.GetAsync(url)).StatusCode;
            Assert.True(status == HttpStatusCode.BadRequest, $"{url} → {(int)status}");
        }
    }

    [SqlServerFact]
    public async Task Reports_RequireAuthentication()
    {
        var anonymous = fixture.Factory.CreateClient();

        var response = await anonymous.GetAsync($"/api/reports?format=csv&from={WeekAgo}&to={Today}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}