using Lighthouse.Api.Data;
using Lighthouse.Api.Infrastructure;
using Lighthouse.Core.AI;
using Lighthouse.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lighthouse.Api.Services.Recommendations;

/// <summary>Повний перерахунок моделі користувача з вагами забування γ^(вік у тижнях) і записом аудиту.</summary>
public sealed class ModelTrainer(LighthouseDbContext db, BanditStore store, ILogger<ModelTrainer> logger)
{
    public Task<ModelAuditEntry> RecomputeAsync(Guid userId, DateTime nowUtc, CancellationToken ct) =>
        OptimisticRetry.RunAsync(db, async () =>
        {
            var since = nowUtc.AddDays(-LinUcbDefaults.RecomputeWindowDays);

            var rows = await db.RecommendationFeedbacks.AsNoTracking()
                .Where(f => f.UserId == userId && f.Reward != null
                            && f.RespondedAtUtc != null && f.RespondedAtUtc >= since)
                .ToListAsync(ct);

            var observations = new List<WeightedObservation>();
            foreach (var row in rows)
            {
                if (ContextCodec.TryParse(row.ContextJson) is not { } context)
                {
                    continue;
                }

                var ageDays = (nowUtc - row.RespondedAtUtc!.Value).TotalDays;
                observations.Add(new WeightedObservation(
                    context, row.Reward!.Value, LinUcbDefaults.ForgettingWeight(ageDays)));
            }

            var model = new LinUcbModel(LinUcbDefaults.Dimension, LinUcbDefaults.Alpha);
            model.Recompute(observations);

            var loaded = await store.LoadAsync(userId, ct);
            var version = store.Stage(userId, new LoadedModel(model, loaded.State), nowUtc);

            var audit = new ModelAuditEntry
            {
                UserId = userId,
                CreatedAtUtc = nowUtc,
                ModelVersion = version,
                EventsCount = observations.Count,
                MeanReward = observations.Count == 0 ? 0.0 : observations.Average(o => o.Reward)
            };
            db.ModelAuditEntries.Add(audit);

            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "Model recomputed for user {UserId}: version {Version}, events {Events}, mean reward {Reward:F3}",
                userId, audit.ModelVersion, audit.EventsCount, audit.MeanReward);

            return audit;
        });
}