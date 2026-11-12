using System.Text.Json;
using Lighthouse.Api.Data;
using Lighthouse.Api.Infrastructure;
using Lighthouse.Api.Services.Behavior;
using Lighthouse.Core.AI;
using Lighthouse.Core.Entities;
using Lighthouse.Shared.Contracts.Recommendations;
using Lighthouse.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Lighthouse.Api.Services.Recommendations;

public sealed class RecommendationService(
    LighthouseDbContext db,
    IPrioritizer prioritizer,
    IUserTimeService userTime,
    UserHistoryBuilder historyBuilder,
    BanditStore store,
    IBehaviorRecorder behavior) : IRecommendationService
{
    /// <summary>Рекомендація без результату довше цього часу вважається неактуальною й видаляється.</summary>
    private static readonly TimeSpan PendingLifetime = TimeSpan.FromDays(14);

    public async Task<RankingResponse> GetRankingAsync(Guid userId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var zone = await userTime.GetZoneAsync(userId, ct);

        await ResolvePendingAsync(userId, now, ct);

        return await OptimisticRetry.RunAsync(db, async () =>
        {
            var tasks = await db.Tasks.AsNoTracking()
                .Where(t => t.UserId == userId && !t.IsDeleted && t.CompletedAtUtc == null)
                .ToListAsync(ct);

            var history = await historyBuilder.BuildAsync(userId, tasks.Select(t => t.Id).ToList(), zone, now, ct);
            var loaded = await store.LoadAsync(userId, ct);
            var feedbackCount = await store.CountFeedbackAsync(userId, ct);

            var result = prioritizer.Rank(new PrioritizationRequest(
                tasks, history, loaded.Model, feedbackCount, now, zone));

            // Для перших K завдань зберігаємо вектор контексту на момент показу та ключ рекомендації.
            var top = result.Ranked.Take(LinUcbDefaults.TopK).ToList();
            var topIds = top.Select(r => r.TaskId).ToList();

            var pending = await db.RecommendationFeedbacks
                .Where(f => f.UserId == userId && f.RespondedAtUtc == null
                            && f.TaskId != null && topIds.Contains(f.TaskId.Value))
                .ToListAsync(ct);

            var keys = new Dictionary<Guid, string>();
            foreach (var ranked in top)
            {
                var row = pending.FirstOrDefault(f => f.TaskId == ranked.TaskId);
                if (row is null)
                {
                    row = new RecommendationFeedback
                    {
                        UserId = userId,
                        TaskId = ranked.TaskId,
                        RecommendationKey = Guid.NewGuid().ToString("N"),
                        CreatedAtUtc = now
                    };
                    db.RecommendationFeedbacks.Add(row);
                }

                row.ContextJson = ContextCodec.Serialize(ranked.Context); // оновлюємо контекст, ключ лишається
                keys[ranked.TaskId] = row.RecommendationKey;
            }

            await db.SaveChangesAsync(ct);

            return new RankingResponse(
                result.Mode.ToString(),
                result.DailyLimit,
                result.Ranked.Select(r => new RankedTaskDto(
                    r.TaskId,
                    r.Position,
                    r.Score,
                    r.IsLayer1,
                    keys.GetValueOrDefault(r.TaskId),
                    r.Explanation.Reasons.Select(x => new ReasonDto(x.Code, x.Text)).ToList(),
                    r.Explanation.UncertaintyBonus,
                    r.Explanation.IsExploration)).ToList(),
                BuildResolutions(result),
                now);
        });
    }

    public Task<FeedbackResponse> SubmitFeedbackAsync(Guid userId, FeedbackRequest request, CancellationToken ct) =>
        OptimisticRetry.RunAsync(db, async () =>
        {
            var feedback = await db.RecommendationFeedbacks
                .FirstOrDefaultAsync(f => f.UserId == userId && f.RecommendationKey == request.RecommendationKey, ct)
                ?? throw new AppException(StatusCodes.Status404NotFound, "Recommendation not found.");

            if (feedback.RespondedAtUtc is not null)
            {
                return new FeedbackResponse(false); // повторний відгук ігноруємо
            }

            var now = DateTime.UtcNow;
            var (kind, eventType) = request.Action switch
            {
                RecommendationAction.Accepted => (FeedbackKind.Accepted, BehaviorEventType.RecommendationAccepted),
                RecommendationAction.Rejected => (FeedbackKind.Rejected, BehaviorEventType.RecommendationRejected),
                RecommendationAction.Postponed => (FeedbackKind.Postponed, BehaviorEventType.RecommendationRejected),
                _ => throw new AppException(StatusCodes.Status400BadRequest, "Unknown action.")
            };

            var loaded = await store.LoadAsync(userId, ct);
            ApplyReward(feedback, RewardCalculator.ForFeedback(kind), loaded.Model, now);
            store.Stage(userId, loaded, now); // конфлікт версії → повтор → відгук уже буде врахований

            var payload = JsonSerializer.Serialize(new
            {
                key = feedback.RecommendationKey,
                action = request.Action.ToString()
            });
            await behavior.RecordAsync(userId, eventType, now, taskId: feedback.TaskId, payloadJson: payload, ct: ct);

            await db.SaveChangesAsync(ct);
            return new FeedbackResponse(true);
        });

    /// <summary>
    /// Визначає результат показаних, але не підтверджених рекомендацій: ручне відкладення (винагорода за
    /// RewardCalculator.ForManualPostponement) або виконання завдання після показу (1).
    /// </summary>
    private Task<int> ResolvePendingAsync(Guid userId, DateTime now, CancellationToken ct) =>
        OptimisticRetry.RunAsync(db, async () =>
        {
            var pending = await db.RecommendationFeedbacks
                .Where(f => f.UserId == userId && f.RespondedAtUtc == null && f.TaskId != null)
                .ToListAsync(ct);
            if (pending.Count == 0)
            {
                return 0;
            }

            var taskIds = pending.Select(f => f.TaskId!.Value).Distinct().ToList();
            var tasks = await db.Tasks.AsNoTracking()
                .Where(t => t.UserId == userId && taskIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, ct);
            var postponements = await db.BehaviorEvents.AsNoTracking()
                .Where(e => e.UserId == userId && e.Type == BehaviorEventType.TaskPostponed
                            && e.TaskId != null && taskIds.Contains(e.TaskId.Value))
                .ToListAsync(ct);

            var loaded = await store.LoadAsync(userId, ct);
            var changed = false;

            foreach (var feedback in pending)
            {
                double? reward = null;
                if (tasks.TryGetValue(feedback.TaskId!.Value, out var task))
                {
                    var firstPostponement = postponements
                        .Where(e => e.TaskId == feedback.TaskId && e.OccurredAtUtc >= feedback.CreatedAtUtc)
                        .OrderBy(e => e.OccurredAtUtc)
                        .FirstOrDefault();

                    if (firstPostponement is not null)
                    {
                        reward = RewardCalculator.ForManualPostponement(
                            firstPostponement.OccurredAtUtc, task.DeadlineUtc, task.CompletedAtUtc, now);
                    }
                    else if (task.CompletedAtUtc is { } completedAt && completedAt >= feedback.CreatedAtUtc)
                    {
                        reward = RewardCalculator.ForFeedback(FeedbackKind.Completed);
                    }
                }

                if (reward is { } value)
                {
                    ApplyReward(feedback, value, loaded.Model, now);
                    changed = true;
                }
                else if (now - feedback.CreatedAtUtc > PendingLifetime)
                {
                    db.RecommendationFeedbacks.Remove(feedback);
                    changed = true;
                }
            }

            if (changed)
            {
                store.Stage(userId, loaded, now);
                await db.SaveChangesAsync(ct);
            }

            return 0;
        });

    private static void ApplyReward(RecommendationFeedback feedback, double reward, LinUcbModel model, DateTime now)
    {
        feedback.Reward = reward;
        feedback.IsPositive = reward >= 1.0;
        feedback.RespondedAtUtc = now;

        if (ContextCodec.TryParse(feedback.ContextJson) is { } context)
        {
            model.Update(context, reward); // Шерман–Моррісон
        }
    }

    private static List<ResolutionDto> BuildResolutions(PrioritizationResult result)
    {
        var resolutions = new List<ResolutionDto>();
        if (result.DeferralSuggestions.Count > 0)
        {
            resolutions.Add(new ResolutionDto(ResolutionKind.DeferToNextDay, result.DeferralSuggestions));
        }

        if (result.DoNotFit.Count > 0)
        {
            resolutions.Add(new ResolutionDto(ResolutionKind.DoesNotFit, result.DoNotFit));
        }

        return resolutions;
    }
}