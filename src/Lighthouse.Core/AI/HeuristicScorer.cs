namespace Lighthouse.Core.AI;

/// <summary>
/// Евристика холодного старту та офлайн-режиму (діє і на клієнті): h = 0.45·x2 + 0.45·x3 + 0.10·x4.
/// </summary>
public static class HeuristicScorer
{
    public const double PriorityWeight = 0.45;
    public const double UrgencyWeight = 0.45;
    public const double OverdueWeight = 0.10;

    public static double Score(double[] x)
    {
        ArgumentNullException.ThrowIfNull(x);
        return PriorityWeight * x[FeatureIndex.Priority]
               + UrgencyWeight * x[FeatureIndex.Urgency]
               + OverdueWeight * x[FeatureIndex.Overdue];
    }

    /// <summary>Внески компонентів у h (інші компоненти — нулі); використовується для пояснень.</summary>
    public static double[] Contributions(double[] x)
    {
        ArgumentNullException.ThrowIfNull(x);
        var contributions = new double[x.Length];
        contributions[FeatureIndex.Priority] = PriorityWeight * x[FeatureIndex.Priority];
        contributions[FeatureIndex.Urgency] = UrgencyWeight * x[FeatureIndex.Urgency];
        contributions[FeatureIndex.Overdue] = OverdueWeight * x[FeatureIndex.Overdue];
        return contributions;
    }
}