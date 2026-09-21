namespace Lighthouse.Shared.Enums;

/// <summary>Типи поведінкових подій, що збираються для AI-модуля (FR-43).</summary>
public enum BehaviorEventType
{
    TaskCreated = 0,
    TaskCompleted = 1,
    TaskPostponed = 2,
    TaskSkipped = 3,
    HabitChecked = 4,
    HabitMissed = 5,
    RecommendationAccepted = 6,
    RecommendationRejected = 7
}
