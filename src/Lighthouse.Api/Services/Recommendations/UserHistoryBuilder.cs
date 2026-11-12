using Lighthouse.Api.Data;
using Lighthouse.Core.AI;
using Lighthouse.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Lighthouse.Api.Services.Recommendations;

/// <summary>
/// Агрегує BehaviorEvents за 28 діб у UserHistory (ознаки x6–x9, ліміт доби). Одиниця згладженої частки —
/// «підсумкові» події TaskCompleted / TaskPostponed / TaskSkipped; c — лише TaskCompleted.
/// </summary>
public sealed class UserHistoryBuilder(LighthouseDbContext db)
{
    private const int WindowDays = 28;

    private static readonly BehaviorEventType[] OutcomeTypes =
        [BehaviorEventType.TaskCompleted, BehaviorEventType.TaskPostponed, BehaviorEventType.TaskSkipped];

    public async Task<UserHistory> BuildAsync(
        Guid userId, IReadOnlyList<Guid> openTaskIds, TimeZoneInfo zone, DateTime nowUtc, CancellationToken ct)
    {
        var since = nowUtc.AddDays(-WindowDays);

        var events = await db.BehaviorEvents.AsNoTracking()
            .Where(e => e.UserId == userId && e.OccurredAtUtc >= since && OutcomeTypes.Contains(e.Type))
            .Select(e => new { e.Type, e.TaskId, e.OccurredAtUtc, e.HourOfDay, e.WeekDay })
            .ToListAsync(ct);

        var eventTaskIds = events.Where(e => e.TaskId != null).Select(e => e.TaskId!.Value).Distinct().ToList();
        var categories = eventTaskIds.Count == 0
            ? new Dictionary<Guid, Guid?>()
            : await db.Tasks.AsNoTracking()
                .Where(t => eventTaskIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.CategoryId, ct);

        var hourDone = new int[UserHistory.HourBuckets];
        var hourTotal = new int[UserHistory.HourBuckets];
        var dayDone = new int[UserHistory.WeekDays];
        var dayTotal = new int[UserHistory.WeekDays];
        var byCategory = new Dictionary<Guid, (int Done, int Total)>();

        foreach (var e in events)
        {
            var completed = e.Type == BehaviorEventType.TaskCompleted;
            var hour = Math.Clamp(e.HourOfDay / 4, 0, UserHistory.HourBuckets - 1);
            var day = Math.Clamp(e.WeekDay, 0, UserHistory.WeekDays - 1);

            hourTotal[hour]++;
            dayTotal[day]++;
            if (completed)
            {
                hourDone[hour]++;
                dayDone[day]++;
            }

            if (e.TaskId is { } taskId && categories.TryGetValue(taskId, out var categoryId) && categoryId is { } cid)
            {
                byCategory.TryGetValue(cid, out var counts);
                byCategory[cid] = (counts.Done + (completed ? 1 : 0), counts.Total + 1);
            }
        }

        var postponeCounts = openTaskIds.Count == 0
            ? new Dictionary<Guid, int>()
            : await db.BehaviorEvents.AsNoTracking()
                .Where(e => e.UserId == userId && e.Type == BehaviorEventType.TaskPostponed
                            && e.TaskId != null && openTaskIds.Contains(e.TaskId.Value))
                .GroupBy(e => e.TaskId!.Value)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Id, x => x.Count, ct);

        var perDay = events
            .Where(e => e.Type == BehaviorEventType.TaskCompleted)
            .GroupBy(e => TimeZoneInfo.ConvertTimeFromUtc(e.OccurredAtUtc, zone).Date)
            .Select(g => g.Count())
            .OrderBy(c => c)
            .ToList();

        return new UserHistory
        {
            ByHourBucket = hourDone.Select((d, i) => new OutcomeCount(d, hourTotal[i])).ToList(),
            ByWeekDay = dayDone.Select((d, i) => new OutcomeCount(d, dayTotal[i])).ToList(),
            ByCategory = byCategory.ToDictionary(kv => kv.Key, kv => new OutcomeCount(kv.Value.Done, kv.Value.Total)),
            PostponeCounts = postponeCounts,
            MedianCompletedPerDay = Median(perDay)
        };
    }

    private static double? Median(List<int> sorted)
    {
        if (sorted.Count == 0)
        {
            return null;
        }

        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2.0;
    }
}