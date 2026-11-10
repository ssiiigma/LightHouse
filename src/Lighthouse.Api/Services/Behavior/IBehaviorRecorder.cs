using Lighthouse.Shared.Enums;

namespace Lighthouse.Api.Services.Behavior;

public interface IBehaviorRecorder
{
    /// <summary>
    /// Додає подію в контекст БД БЕЗ SaveChanges: вона зберігається тією ж транзакцією, що й зміна, яку описує.
    /// HourOfDay і WeekDay (пн = 0) рахуються за часовим поясом користувача.
    /// </summary>
    Task RecordAsync(
        Guid userId,
        BehaviorEventType type,
        DateTime occurredAtUtc,
        Guid? taskId = null,
        Guid? habitId = null,
        string? payloadJson = null,
        CancellationToken ct = default);
}