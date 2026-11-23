using Lighthouse.Api.Services.Push;

namespace Lighthouse.Api.BackgroundJobs;

/// <summary>Щохвилини перевіряє, кому час надіслати щоденний огляд чи розумне нагадування.</summary>
public sealed class PushWorker(IServiceScopeFactory scopes, ILogger<PushWorker> logger)
    : PeriodicWorker(scopes, logger)
{
    private readonly ILogger<PushWorker> _logger = logger;

    protected override string Name => "Push worker";

    protected override TimeSpan Period => TimeSpan.FromMinutes(1);

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var sent = await services.GetRequiredService<PushScheduler>().RunAsync(DateTime.UtcNow, ct);
        if (sent > 0)
        {
            _logger.LogInformation("Push notifications sent: {Count}.", sent);
        }
    }
}