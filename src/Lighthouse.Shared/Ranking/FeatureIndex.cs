namespace Lighthouse.Shared.Ranking;

/// <summary>Індекси компонентів вектора ознак x (нумерація з нуля: x1 → 0, …, x10 → 9).</summary>
public static class FeatureIndex
{
    public const int Bias = 0;
    public const int Priority = 1;
    public const int Urgency = 2;
    public const int Overdue = 3;
    public const int Estimate = 4;
    public const int HourBucketRate = 5;
    public const int WeekDayRate = 6;
    public const int CategoryRate = 7;
    public const int Postpone = 8;
    public const int Reminder = 9;
}