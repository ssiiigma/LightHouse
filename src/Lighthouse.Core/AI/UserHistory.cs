namespace Lighthouse.Core.AI;

/// <summary>Кількість завершених і всіх «підсумкових» подій (виконано / відкладено / пропущено) у групі.</summary>
public readonly record struct OutcomeCount(int Completed, int Total)
{
    /// <summary>Згладжена частка виконаних (c + 1) / (n + 2); без даних = 0.5.</summary>
    public double SmoothedRate => (Completed + 1.0) / (Total + 2.0);
}

/// <summary>
/// Агрегована історія користувача для ознак x6–x9 і денного ліміту. Збирається сервером з BehaviorEvents:
/// години/дні тижня — у часовому поясі користувача; категорії — за останні 28 діб.
/// </summary>
public sealed class UserHistory
{
    public const int HourBuckets = 6;
    public const int WeekDays = 7;

    /// <summary>Чотиригодинні інтервали доби: 0 = 00–04, …, 5 = 20–24.</summary>
    public IReadOnlyList<OutcomeCount> ByHourBucket { get; init; } = new OutcomeCount[HourBuckets];

    /// <summary>Дні тижня: понеділок = 0 … неділя = 6.</summary>
    public IReadOnlyList<OutcomeCount> ByWeekDay { get; init; } = new OutcomeCount[WeekDays];

    public IReadOnlyDictionary<Guid, OutcomeCount> ByCategory { get; init; } = new Dictionary<Guid, OutcomeCount>();

    /// <summary>Скільки разів завдання відкладалось.</summary>
    public IReadOnlyDictionary<Guid, int> PostponeCounts { get; init; } = new Dictionary<Guid, int>();

    /// <summary>Медіана виконаних завдань за добу (за 28 діб, лише доби з активністю); null — історії немає (новий користувач).</summary>
    public double? MedianCompletedPerDay { get; init; }

    public static UserHistory Empty { get; } = new();
}