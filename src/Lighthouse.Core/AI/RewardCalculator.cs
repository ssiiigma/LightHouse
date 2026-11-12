namespace Lighthouse.Core.AI;

public enum FeedbackKind
{
    Accepted,
    Completed,
    Rejected,
    Postponed
}

/// <summary>
/// Винагорода: 1 — прийнято / виконано; 0 — відхилено / відкладено. Для ручного відкладення: 1, якщо завдання
/// виконано до дедлайну (без дедлайну — протягом 3 діб після відкладення), інакше 0; доки результат невідомий — null.
/// </summary>
public static class RewardCalculator
{
    public static readonly TimeSpan NoDeadlineHorizon = TimeSpan.FromDays(3);

    public static double ForFeedback(FeedbackKind kind) =>
        kind is FeedbackKind.Accepted or FeedbackKind.Completed ? 1.0 : 0.0;

    public static double? ForManualPostponement(
        DateTime postponedAtUtc, DateTime? deadlineUtc, DateTime? completedAtUtc, DateTime nowUtc)
    {
        var horizon = deadlineUtc is { } deadline
            ? ZoneTime.ToUtc(deadline)
            : ZoneTime.ToUtc(postponedAtUtc) + NoDeadlineHorizon;

        if (completedAtUtc is { } completed)
        {
            return ZoneTime.ToUtc(completed) <= horizon ? 1.0 : 0.0;
        }

        return ZoneTime.ToUtc(nowUtc) > horizon ? 0.0 : null;
    }
}