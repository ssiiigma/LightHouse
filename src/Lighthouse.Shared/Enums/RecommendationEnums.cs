namespace Lighthouse.Shared.Enums;

/// <summary>Реакція користувача на рекомендацію (POST /api/recommendations/feedback).</summary>
public enum RecommendationAction
{
    Accepted = 0,
    Rejected = 1,
    Postponed = 2
}

/// <summary>Тип варіанта вирішення, який пропонує система.</summary>
public enum ResolutionKind
{
    /// <summary>Перенести зазначені завдання на наступну добу (перевищено денний ліміт).</summary>
    DeferToNextDay = 0,

    /// <summary>Зазначені завдання не вкладаються в час до дедлайну.</summary>
    DoesNotFit = 1
}