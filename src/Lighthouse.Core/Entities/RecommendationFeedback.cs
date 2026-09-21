namespace Lighthouse.Core.Entities;

/// <summary>Реакція користувача на AI-рекомендацію (FR-39, FR-42) — винагорода для LinUCB.</summary>
public class RecommendationFeedback
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? TaskId { get; set; }
    public string RecommendationKey { get; set; } = string.Empty;
    public bool IsPositive { get; set; }

    /// <summary>Вектор контекстних ознак на момент показу рекомендації (JSON).</summary>
    public string? ContextJson { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
