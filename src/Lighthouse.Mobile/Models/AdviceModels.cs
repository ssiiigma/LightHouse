namespace Lighthouse.Mobile.Models;

public enum AdviceKind
{
    Focus,
    Reschedule,
    DoesNotFit,
    Defer
}

/// <summary>Порада в банері: рядки вже локалізовані на момент побудови.</summary>
public sealed class AdviceItem
{
    public required string Id { get; init; }
    public required AdviceKind Kind { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public IReadOnlyList<Guid> TaskIds { get; init; } = [];
    public string? RecommendationKey { get; init; }
    public DateTime? ProposedReminderUtc { get; init; }

    /// <summary>true — порада вимагає відповіді або дії (є кнопки).</summary>
    public bool IsActionable { get; init; }
    public string ApplyText { get; init; } = string.Empty;
    public string DismissText { get; init; } = string.Empty;
}