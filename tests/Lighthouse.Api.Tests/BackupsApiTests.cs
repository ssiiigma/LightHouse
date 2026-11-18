using System.Net;
using System.Net.Http.Json;
using Lighthouse.Shared.Contracts.Backups;
using Lighthouse.Shared.Contracts.Habits;
using Lighthouse.Shared.Contracts.Tasks;
using Lighthouse.Shared.Enums;

namespace Lighthouse.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class BackupsApiTests(ApiFixture fixture)
{
    private static async Task<BackupInfoDto> CreateBackupAsync(HttpClient client)
    {
        var response = await client.PostAsync("/api/backups", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BackupInfoDto>())!;
    }

    private static async Task PutTaskAsync(HttpClient client, Guid id, string title)
    {
        await Task.Delay(20); // clientUpdatedAtUtc має зростати
        var response = await client.PutAsJsonAsync($"/api/tasks/{id}", TestApi.NewTask(title));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<List<TaskDto>> TasksAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<TaskDto>>("/api/tasks"))!;

    [SqlServerFact]
    public async Task Restore_ReplacesData_LeavesSafetyCopy_AndPublishesTombstones()
    {
        var user = await fixture.Factory.RegisterAsync();
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();
        await PutTaskAsync(user.Client, a, "Alpha");
        await PutTaskAsync(user.Client, b, "Beta");
        var backup = await CreateBackupAsync(user.Client);

        await PutTaskAsync(user.Client, a, "Alpha changed");
        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.DeleteAsync($"/api/tasks/{b}")).StatusCode);
        await PutTaskAsync(user.Client, c, "Gamma");

        var before = DateTime.UtcNow.AddSeconds(-1);
        var restore = await user.Client.PostAsync($"/api/backups/{backup.Id}/restore", null);
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        var result = (await restore.Content.ReadFromJsonAsync<RestoreResultDto>())!;
        Assert.Equal(2, result.Tasks);

        var tasks = await TasksAsync(user.Client);
        Assert.Equal(new[] { "Alpha", "Beta" }, tasks.Select(t => t.Title).OrderBy(t => t).ToArray());

        var list = (await user.Client.GetFromJsonAsync<List<BackupInfoDto>>("/api/backups"))!;
        Assert.Equal(2, list.Count);
        Assert.Contains(list, x => x.Id == result.SafetyBackupId);

        var delta = (await user.Client.GetFromJsonAsync<List<TaskDto>>(
            "/api/tasks?modifiedSince=" + Uri.EscapeDataString(before.ToString("O"))))!;
        Assert.Contains(delta, t => t.Id == c && t.IsDeleted);
    }

    [SqlServerFact]
    public async Task Restore_BringsBackHabitCheckIns()
    {
        var user = await fixture.Factory.RegisterAsync();
        var habit = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        await user.Client.PutAsJsonAsync($"/api/habits/{habit}", TestApi.NewHabit("Run"));
        await user.Client.PutAsync($"/api/habits/{habit}/logs/{today}", null);
        var backup = await CreateBackupAsync(user.Client);

        await user.Client.DeleteAsync($"/api/habits/{habit}/logs/{today}");
        var removed = (await user.Client.GetFromJsonAsync<List<HabitDto>>("/api/habits"))!;
        Assert.Empty(Assert.Single(removed).Logs);

        Assert.Equal(HttpStatusCode.OK, (await user.Client.PostAsync($"/api/backups/{backup.Id}/restore", null)).StatusCode);

        var restored = (await user.Client.GetFromJsonAsync<List<HabitDto>>("/api/habits"))!;
        Assert.Single(Assert.Single(restored).Logs);
    }

    [SqlServerFact]
    public async Task Download_ReturnsJsonWithTheData()
    {
        var user = await fixture.Factory.RegisterAsync();
        await PutTaskAsync(user.Client, Guid.NewGuid(), "Downloadable");
        var backup = await CreateBackupAsync(user.Client);

        var response = await user.Client.GetAsync($"/api/backups/{backup.Id}/download");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Downloadable", await response.Content.ReadAsStringAsync());
    }

    [SqlServerFact]
    public async Task ForeignBackup_Returns404_ForDownloadRestoreAndDelete()
    {
        var owner = await fixture.Factory.RegisterAsync();
        var intruder = await fixture.Factory.RegisterAsync();
        await PutTaskAsync(owner.Client, Guid.NewGuid(), "Private");
        var backup = await CreateBackupAsync(owner.Client);

        Assert.Equal(HttpStatusCode.NotFound, (await intruder.Client.GetAsync($"/api/backups/{backup.Id}/download")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.Client.PostAsync($"/api/backups/{backup.Id}/restore", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.Client.DeleteAsync($"/api/backups/{backup.Id}")).StatusCode);
        Assert.Single((await owner.Client.GetFromJsonAsync<List<BackupInfoDto>>("/api/backups"))!);
    }

    [SqlServerFact]
    public async Task ManualBackups_AreLimitedToTen_AndDeleteFreesASlot()
    {
        var user = await fixture.Factory.RegisterAsync();
        BackupInfoDto? first = null;
        for (var i = 0; i < 10; i++)
        {
            first ??= await CreateBackupAsync(user.Client);
            if (i > 0) await CreateBackupAsync(user.Client);
        }

        Assert.Equal(HttpStatusCode.Conflict, (await user.Client.PostAsync("/api/backups", null)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.DeleteAsync($"/api/backups/{first!.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.Client.PostAsync("/api/backups", null)).StatusCode);
    }

    [SqlServerFact]
    public async Task Backups_RequireAuthentication()
    {
        var anonymous = fixture.Factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/backups")).StatusCode);
    }
}