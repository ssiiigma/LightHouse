namespace Lighthouse.Core.AI;

/// <summary>Параметри адаптивної пріоритизації (специфікація 3.3.1).</summary>
public static class LinUcbDefaults
{
    public const int Dimension = 10;
    public const double Alpha = 0.5;
    public const double Lambda = 1.0;

    /// <summary>Поки подій відгуку менше, застосовується евристика (холодний старт).</summary>
    public const int MinFeedbackEvents = 20;

    /// <summary>Кількість рекомендованих завдань, для яких зберігається контекст і збирається відгук.</summary>
    public const int TopK = 3;

    /// <summary>Коефіцієнт забування за тиждень.</summary>
    public const double Gamma = 0.9;

    public const int RecomputeWindowDays = 90;

    /// <summary>Вага спостереження γ^(вік у повних тижнях).</summary>
    public static double ForgettingWeight(double ageDays) =>
        Math.Pow(Gamma, Math.Floor(Math.Max(0.0, ageDays) / 7.0));
}