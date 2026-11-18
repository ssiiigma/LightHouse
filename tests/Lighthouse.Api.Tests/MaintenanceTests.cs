using System.Net;
using System.Net.Http.Json;
using Lighthouse.Api.Data;
using Lighthouse.Api.Services.Backups;
using Lighthouse.Shared.Contracts.Backups;
using Lighthouse.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lighthouse.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class MaintenanceTests(ApiFixture fixture)
{
    private async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        return await action(scope.ServiceProvider);
    }

    private Task<CleanupResult> PurgeAsync(DateTime nowUtc) =>
        InScopeAsync(sp => sp.GetRequiredService<ArchiveCleaner>().PurgeAsync(nowUtc, CancellationToken.None));

    private Task<bool> TaskExistsAsync(Guid id) =>
        InScopeAsync(sp => sp.GetRequiredService<LighthouseDbContext>().Tasks.AnyAsync(t => t.Id == id));

    [SqlServerFact]
    public async Task Purge_RemovesArchivedItemsAfterFourteenDays_AndKeepsTheRest()
    {
        var user = await fixture.Factory.RegisterAsync();
        Guid deleted = Guid.NewGuid(), kept = Guid.NewGuid(), category = Guid.NewGuid();
        await user.Client.PutAsJsonAsync($"/api/tasks/{deleted}", TestApi.NewTask("To purge"));
        await user.Client.PutAsJsonAsync($"/api/tasks/{kept}", TestApi.NewTask("To keep"));
        await user.Client.PutAsJsonAsync($"/api/categories/{category}", TestApi.NewCategory("Old"));
        await user.Client.DeleteAsync($"/api/tasks/{deleted}");
        await user.Client.DeleteAsync($"/api/categories/{category}");

        await PurgeAsync(DateTime.UtcNow.AddDays(1));
        Assert.True(await TaskExistsAsync(deleted)); // ще в архіві

        await PurgeAsync(DateTime.UtcNow.AddDays(15));
        Assert.False(await TaskExistsAsync(deleted));
        Assert.True(await TaskExistsAsync(kept));
        var categoryExists = await InScopeAsync(sp =>
            sp.GetRequiredService<LighthouseDbContext>().Categories.AnyAsync(c => c.Id == category));
        Assert.False(categoryExists);
    }

    [SqlServerFact]
    public async Task Scheduler_CreatesOneAutoBackupPerDay_OnlyWhenDataChanged()
    {
        var user = await fixture.Factory.RegisterAsync();
        var task = Guid.NewGuid();
        await user.Client.PutAsJsonAsync($"/api/tasks/{task}", TestApi.NewTask("Scheduled"));

        async Task RunAsync(DateTime now) =>
            await InScopeAsync(async sp => await sp.GetRequiredService<BackupScheduler>().RunAsync(now, CancellationToken.None));

        async Task<int> AutoCountAsync() =>
            (await user.Client.GetFromJsonAsync<List<BackupInfoDto>>("/api/backups"))!.Count(b => b.Kind == BackupKind.Auto);

        var now = DateTime.UtcNow;
        await RunAsync(now);
        Assert.Equal(1, await AutoCountAsync());

        await RunAsync(now.AddHours(1)); // менше доби
        Assert.Equal(1, await AutoCountAsync());

        await Task.Delay(20);
        await user.Client.PutAsJsonAsync($"/api/tasks/{task}", TestApi.NewTask("Changed"));
        await RunAsync(now.AddHours(25));
        Assert.Equal(2, await AutoCountAsync());
    }

    [SqlServerFact]
    public async Task AutoBackups_KeepOnlyTheLastSeven()
    {
        var user = await fixture.Factory.RegisterAsync();
        await user.Client.PutAsJsonAsync($"/api/tasks/{Guid.NewGuid()}", TestApi.NewTask("Data"));

        for (var i = 0; i < 9; i++)
        {
            await InScopeAsync(async sp => await sp.GetRequiredService<IBackupService>().CreateAsync(
                user.Auth.User.Id, BackupKind.Auto, false, DateTime.UtcNow.AddMinutes(i), CancellationToken.None));
        }

        var list = (await user.Client.GetFromJsonAsync<List<BackupInfoDto>>("/api/backups"))!;
        Assert.Equal(7, list.Count(b => b.Kind == BackupKind.Auto));
    }
}