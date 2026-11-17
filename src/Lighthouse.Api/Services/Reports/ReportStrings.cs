namespace Lighthouse.Api.Services.Reports;

/// <summary>Тексти звіту двома мовами (мова береться з параметра запиту; за замовчуванням українська).</summary>
internal sealed class ReportStrings
{
    public string Title { get; init; } = "";
    public string Period { get; init; } = "";
    public string User { get; init; } = "";
    public string Generated { get; init; } = "";
    public string CompletionRate { get; init; } = "";
    public string CompletedCount { get; init; } = "";
    public string HabitRegularity { get; init; } = "";
    public string BestStreak { get; init; } = "";
    public string PerDay { get; init; } = "";
    public string PerMonth { get; init; } = "";
    public string Categories { get; init; } = "";
    public string Habits { get; init; } = "";
    public string TasksSection { get; init; } = "";
    public string NoData { get; init; } = "";
    public string ColDate { get; init; } = "";
    public string ColTask { get; init; } = "";
    public string ColCategory { get; init; } = "";
    public string ColPriority { get; init; } = "";
    public string ColHabit { get; init; } = "";
    public string ColCount { get; init; } = "";
    public string ColShare { get; init; } = "";
    public string ColCheckIns { get; init; } = "";
    public string ColCurrentStreak { get; init; } = "";
    public string ColBestStreak { get; init; } = "";
    public string NoCategory { get; init; } = "";
    public string Page { get; init; } = "";
    public string Truncated { get; init; } = "";
    public string Low { get; init; } = "";
    public string Medium { get; init; } = "";
    public string High { get; init; } = "";
    public string Critical { get; init; } = "";

    public static ReportStrings For(string? language) =>
        string.Equals(language, "en", StringComparison.OrdinalIgnoreCase) ? English : Ukrainian;

    private static readonly ReportStrings Ukrainian = new()
    {
        Title = "Звіт про продуктивність", Period = "Період", User = "Користувач", Generated = "Сформовано",
        CompletionRate = "Виконання завдань", CompletedCount = "Виконано завдань",
        HabitRegularity = "Регулярність звичок", BestStreak = "Найкраща серія, дн.",
        PerDay = "Виконані завдання за днями", PerMonth = "Виконані завдання за місяцями",
        Categories = "Розподіл за категоріями", Habits = "Звички", TasksSection = "Виконані завдання",
        NoData = "Немає даних за цей період.", ColDate = "Дата", ColTask = "Завдання", ColCategory = "Категорія",
        ColPriority = "Пріоритет", ColHabit = "Звичка", ColCount = "Кількість", ColShare = "Частка",
        ColCheckIns = "Відміток", ColCurrentStreak = "Серія", ColBestStreak = "Найкраща",
        NoCategory = "Без категорії", Page = "Сторінка", Truncated = "Показано перші {0} із {1} завдань.",
        Low = "Низький", Medium = "Середній", High = "Високий", Critical = "Критичний"
    };

    private static readonly ReportStrings English = new()
    {
        Title = "Productivity report", Period = "Period", User = "User", Generated = "Generated",
        CompletionRate = "Task completion", CompletedCount = "Tasks completed",
        HabitRegularity = "Habit regularity", BestStreak = "Best streak, days",
        PerDay = "Completed tasks per day", PerMonth = "Completed tasks per month",
        Categories = "Category distribution", Habits = "Habits", TasksSection = "Completed tasks",
        NoData = "No data for this period.", ColDate = "Date", ColTask = "Task", ColCategory = "Category",
        ColPriority = "Priority", ColHabit = "Habit", ColCount = "Count", ColShare = "Share",
        ColCheckIns = "Check-ins", ColCurrentStreak = "Streak", ColBestStreak = "Best",
        NoCategory = "No category", Page = "Page", Truncated = "Showing the first {0} of {1} tasks.",
        Low = "Low", Medium = "Medium", High = "High", Critical = "Critical"
    };
}