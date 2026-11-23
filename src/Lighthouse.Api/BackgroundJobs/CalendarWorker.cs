using Lighthouse.Api.Services.Calendar;

namespace Lighthouse.Api.BackgroundJobs;

/// <summary>Кожні 2 хвилини синхронізує календарі всіх підключених користувачів.</summary>
public sealed class CalendarWorker(IServiceScopeFactory scopes, ILogger<CalendarWorker> logger)
    : PeriodicWorker(scopes, logger)
{
    private readonly ILogger<CalendarWorker> _logger = logger;

    protected override string Name => "Calendar worker";

    protected override TimeSpan Period => TimeSpan.FromMinutes(1);

    protected override TimeSpan InitialDelay => TimeSpan.FromSeconds(45);

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
        var userIds = await services.GetRequiredService<ICalendarStore>().ConnectedUserIdsAsync(ct);

        foreach (var userId in userIds)
        {
            try
            {
                // Окремий контекст БД для кожного користувача: збій одного не впливає на інших.
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ICalendarSyncService>().SyncAsync(userId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Calendar sync failed for user {UserId}.", userId);
            }
        }
    }
}