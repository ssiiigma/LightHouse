using Lighthouse.Shared.Enums;
using Lighthouse.Shared.Notifications;
using Lighthouse.Shared.Ranking;
using Lighthouse.Shared.Scheduling;

namespace Lighthouse.Api.Services.Push;

/// <summary>
/// Що надіслати користувачеві зараз. Огляд: з 08:00 протягом 2 годин, якщо є завдання на сьогодні чи прострочені.
/// Розумне нагадування: протягом години від найпродуктивнішого часу, якщо є відкриті High/Critical. Не в тихому режимі,
/// не частіше разу на добу кожного виду, огляд має перевагу над розумним нагадуванням у тому самому запуску.
/// </summary>
public static class PushPlanner
{
    public static readonly TimeSpan DigestWindow = TimeSpan.FromHours(2);
    public static readonly TimeSpan SmartWindow = TimeSpan.FromHours(1);

    public static IReadOnlyList<PlannedPush> Plan(PushUserSnapshot s)
    {
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(s.NowUtc, s.Zone);
        if (IsQuiet(nowLocal, s))
        {
            return [];
        }

        var time = nowLocal.TimeOfDay;

        if (s.DigestEnabled && !s.DigestSentToday && time >= s.DigestAt && time < s.DigestAt + DigestWindow)
        {
            var today = nowLocal.Date;
            var due = s.OpenTasks
                .Where(t => t.DeadlineUtc is { } d && TimeZoneInfo.ConvertTimeFromUtc(AsUtc(d), s.Zone).Date <= today)
                .ToList();
            if (due.Count > 0)
            {
                return [new PlannedPush(PushKind.Digest, due.Count, Top(due, s.NowUtc))];
            }
        }

        if (s.SmartEnabled && !s.SmartSentToday)
        {
            var completedLocal = s.CompletedUtc.Select(c => TimeZoneInfo.ConvertTimeFromUtc(AsUtc(c), s.Zone));
            if (ScheduleAdvisor.SuggestTime(nowLocal, completedLocal) is { } best && time >= best && time < best + SmartWindow)
            {
                var important = s.OpenTasks.Where(t => t.Priority >= TaskPriority.High).ToList();
                if (important.Count > 0)
                {
                    return [new PlannedPush(PushKind.Smart, important.Count, Top(important, s.NowUtc))];
                }
            }
        }

        return [];
    }

    private static bool IsQuiet(DateTime nowLocal, PushUserSnapshot s) =>
        s.QuietStart is { } start && s.QuietEnd is { } end
        && QuietHours.Shift(nowLocal, new NotificationPrefs(true, true, true, start.ToTimeSpan(), end.ToTimeSpan())) != nowLocal;

    private static PushTask Top(IReadOnlyList<PushTask> tasks, DateTime nowUtc)
    {
        var ranked = OfflineRanker.Rank(tasks.Select(t => new OfflineTask(t.Id, t.Priority, t.DeadlineUtc)), nowUtc);
        return tasks.First(t => t.Id == ranked[0].TaskId);
    }

    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}