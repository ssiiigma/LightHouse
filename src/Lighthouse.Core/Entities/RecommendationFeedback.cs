namespace Lighthouse.Core.Entities;

/// <summary>
/// Показана рекомендація та реакція на неї (FR-39, FR-42). Запис створюється під час показу (разом із
/// вектором контексту на цей момент); відгук або результат (виконання, ручне відкладення) заповнює
/// <see cref="RespondedAtUtc"/> і <see cref="Reward"/>. Повторний відгук із тим самим ключем ігнорується.
/// </summary>
public class RecommendationFeedback
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? TaskId { get; set; }
    public string RecommendationKey { get; set; } = string.Empty;
    public bool IsPositive { get; set; }

    /// <summary>Вектор контекстних ознак на момент показу рекомендації (JSON-масив із 10 чисел).</summary>
    public string? ContextJson { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>null — рекомендацію показано, відгуку ще немає.</summary>
    public DateTime? RespondedAtUtc { get; set; }

    /// <summary>Винагорода для LinUCB: 1 або 0; null, поки відгуку немає.</summary>
    public double? Reward { get; set; }
}