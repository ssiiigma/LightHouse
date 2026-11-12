using Lighthouse.Api.Data;
using Lighthouse.Api.Services.Recommendations;
using Lighthouse.Core.AI;
using Microsoft.EntityFrameworkCore;

namespace Lighthouse.Api.BackgroundJobs;

/// <summary>
/// Щотижневий перерахунок моделей користувачів. Щогодини перевіряє, чи минуло 7 діб від останнього запису
/// в ModelAuditEntries; час попереднього запуску переживає перезапуск контейнера.
/// </summary>
public sealed class ModelTrainingWorker(IServiceScopeFactory scopes, ILogger<ModelTrainingWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromDays(7);
    private static readonly TimeSpan CheckPeriod = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunIfDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Weekly model recompute failed; will retry on the next check.");
            }

            try
            {
                await Task.Delay(CheckPeriod, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task RunIfDueAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LighthouseDbContext>();
        var trainer = scope.ServiceProvider.GetRequiredService<ModelTrainer>();

        var now = DateTime.UtcNow;
        var lastRun = await db.ModelAuditEntries.MaxAsync(a => (DateTime?)a.CreatedAtUtc, ct);

        if (lastRun is { } last && now - last < Interval)
        {
            return;
        }

        var since = now.AddDays(-LinUcbDefaults.RecomputeWindowDays);
        var userIds = await db.RecommendationFeedbacks.AsNoTracking()
            .Where(f => f.Reward != null && f.RespondedAtUtc >= since)
            .Select(f => f.UserId)
            .Distinct()
            .ToListAsync(ct);

        var totalEvents = 0;
        foreach (var userId in userIds)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                totalEvents += (await trainer.RecomputeAsync(userId, now, ct)).EventsCount;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Recompute failed for user {UserId}.", userId);
            }
        }

        if (userIds.Count > 0)
        {
            logger.LogInformation(
                "Weekly model recompute finished: {Users} users, {Events} events.", userIds.Count, totalEvents);
        }
    }
}