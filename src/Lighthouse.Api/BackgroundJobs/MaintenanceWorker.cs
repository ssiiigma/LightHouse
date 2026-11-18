using Lighthouse.Api.Services.Backups;

namespace Lighthouse.Api.BackgroundJobs;

/// <summary>Остаточне видалення архіву (14 діб) та прострочених токенів.</summary>
public sealed class MaintenanceWorker(IServiceScopeFactory scopes, ILogger<MaintenanceWorker> logger)
    : PeriodicWorker(scopes, logger)
{
    private readonly ILogger<MaintenanceWorker> _logger = logger;

    protected override string Name => "Maintenance worker";

    protected override TimeSpan Period => TimeSpan.FromHours(6);

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var result = await services.GetRequiredService<ArchiveCleaner>().PurgeAsync(DateTime.UtcNow, ct);
        if (result.Tasks + result.Habits + result.Categories + result.Tokens > 0)
        {
            _logger.LogInformation(
                "Archive cleanup: tasks {Tasks}, habits {Habits}, categories {Categories}, tokens {Tokens}.",
                result.Tasks, result.Habits, result.Categories, result.Tokens);
        }
    }
}