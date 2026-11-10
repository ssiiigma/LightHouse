using Lighthouse.Api.Data;
using Lighthouse.Api.Infrastructure;
using Lighthouse.Core.Entities;
using Lighthouse.Shared.Enums;

namespace Lighthouse.Api.Services.Behavior;

public sealed class BehaviorRecorder(LighthouseDbContext db, IUserTimeService userTime) : IBehaviorRecorder
{
    public async Task RecordAsync(
        Guid userId,
        BehaviorEventType type,
        DateTime occurredAtUtc,
        Guid? taskId = null,
        Guid? habitId = null,
        string? payloadJson = null,
        CancellationToken ct = default)
    {
        var zone = await userTime.GetZoneAsync(userId, ct);
        var utc = SyncTime.ToUtc(occurredAtUtc);
        var local = UserTime.ToLocal(utc, zone);

        db.BehaviorEvents.Add(new BehaviorEvent
        {
            UserId = userId,
            Type = type,
            TaskId = taskId,
            HabitId = habitId,
            OccurredAtUtc = utc,
            HourOfDay = local.Hour,
            WeekDay = UserTime.DayIndex(local),
            PayloadJson = payloadJson
        });
    }
}