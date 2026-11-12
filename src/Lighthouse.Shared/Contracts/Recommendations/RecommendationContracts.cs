using Lighthouse.Shared.Enums;

namespace Lighthouse.Shared.Contracts.Recommendations;

/// <summary>Причина рекомендації: стабільний код (для локалізації на клієнті) і шаблонний текст.</summary>
public sealed record ReasonDto(string Code, string Text);

/// <param name="RecommendationKey">Ключ для відгуку; заповнений лише для перших K = 3 завдань.</param>
/// <param name="UncertaintyBonus">Бонус невизначеності окремо від причин (дослідження).</param>
public sealed record RankedTaskDto(
    Guid TaskId,
    int Position,
    double Score,
    bool IsLayer1,
    string? RecommendationKey,
    IReadOnlyList<ReasonDto> Reasons,
    double UncertaintyBonus,
    bool IsExploration);

/// <summary>Варіант вирішення: що саме система пропонує зробити із зазначеними завданнями.</summary>
public sealed record ResolutionDto(ResolutionKind Kind, IReadOnlyList<Guid> TaskIds);

/// <param name="Mode">"Heuristic" (холодний старт) або "LinUcb".</param>
public sealed record RankingResponse(
    string Mode,
    int DailyLimit,
    IReadOnlyList<RankedTaskDto> Ranked,
    IReadOnlyList<ResolutionDto> Resolutions,
    DateTime GeneratedAtUtc);

public sealed record FeedbackRequest(string RecommendationKey, RecommendationAction Action);

/// <param name="Applied">false, якщо відгук із цим ключем уже було враховано (повторний ігнорується).</param>
public sealed record FeedbackResponse(bool Applied);