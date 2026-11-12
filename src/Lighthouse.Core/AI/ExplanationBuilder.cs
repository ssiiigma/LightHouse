namespace Lighthouse.Core.AI;

/// <summary>Причина рекомендації: стабільний код (для локалізації на клієнті) і шаблонний текст українською.</summary>
public sealed record ExplanationReason(string Code, string Text);

/// <param name="Reasons">До двох найбільших додатних внесків cⱼ = θⱼ·xⱼ.</param>
/// <param name="UncertaintyBonus">Бонус за невизначеність α·√(xᵀA⁻¹x) — окремо від причин.</param>
/// <param name="IsExploration">true, якщо завдання піднято переважно через невизначеність моделі.</param>
public sealed record Explanation(
    IReadOnlyList<ExplanationReason> Reasons, double UncertaintyBonus, bool IsExploration);

public static class ExplanationBuilder
{
    private const int MaxReasons = 2;
    private const double Epsilon = 1e-9;

    /// <summary>
    /// Пояснення за вектором внесків (θⱼ·xⱼ або внески евристики). Зміщення x1 не розглядається.
    /// </summary>
    public static Explanation Build(double[] contributions, double[] x, double uncertaintyBonus, double expectedReward)
    {
        ArgumentNullException.ThrowIfNull(contributions);
        ArgumentNullException.ThrowIfNull(x);

        var reasons = new List<ExplanationReason>();

        var ordered = Enumerable.Range(FeatureIndex.Priority, contributions.Length - FeatureIndex.Priority)
            .Where(j => contributions[j] > Epsilon)
            .OrderByDescending(j => contributions[j])
            .ThenBy(j => j);

        foreach (var j in ordered)
        {
            var reason = ReasonFor(j, x);
            if (reason is null || reasons.Any(r => r.Code == reason.Code))
            {
                continue;
            }

            reasons.Add(reason);
            if (reasons.Count == MaxReasons)
            {
                break;
            }
        }

        var isExploration = uncertaintyBonus > Math.Max(expectedReward, 0.0) + Epsilon;
        return new Explanation(reasons, uncertaintyBonus, isExploration);
    }

    private static ExplanationReason? ReasonFor(int index, double[] x) => index switch
    {
        FeatureIndex.Priority => new("priority", "високий пріоритет завдання"),
        FeatureIndex.Urgency when x[FeatureIndex.Overdue] >= 1.0 => new("overdue", "завдання вже прострочене"),
        FeatureIndex.Urgency when x[FeatureIndex.Urgency] >= 0.5 => new("deadline_within_day", "до дедлайну менше доби"),
        FeatureIndex.Urgency => new("deadline_approaching", "дедлайн наближається"),
        FeatureIndex.Overdue => new("overdue", "завдання вже прострочене"),
        FeatureIndex.Estimate => new("duration", "враховано орієнтовну тривалість завдання"),
        FeatureIndex.HourBucketRate => new("usual_time", "зазвичай ви виконуєте такі завдання в цей час"),
        FeatureIndex.WeekDayRate => new("usual_weekday", "зазвичай ви виконуєте завдання в цей день тижня"),
        FeatureIndex.CategoryRate => new("usual_category", "завдання цієї категорії ви зазвичай виконуєте"),
        FeatureIndex.Postpone => new("postponed", "завдання вже відкладалось"),
        FeatureIndex.Reminder => new("has_reminder", "для завдання налаштовано нагадування"),
        _ => null
    };
}