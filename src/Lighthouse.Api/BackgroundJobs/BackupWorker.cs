using Lighthouse.Api.Services.Backups;

namespace Lighthouse.Api.BackgroundJobs;

public sealed class BackupWorker(IServiceScopeFactory scopes, ILogger<BackupWorker> logger)
    : PeriodicWorker(scopes, logger)
{
    private readonly ILogger<BackupWorker> _logger = logger;

    protected override string Name => "Backup worker";

    protected override TimeSpan Period => TimeSpan.FromHours(1);

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var created = await services.GetRequiredService<BackupScheduler>().RunAsync(DateTime.UtcNow, ct);
        if (created > 0)
        {
            _logger.LogInformation("Automatic backups created: {Count}.", created);
        }
    }
}