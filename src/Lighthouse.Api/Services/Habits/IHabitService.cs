using Lighthouse.Shared.Contracts.Habits;

namespace Lighthouse.Api.Services.Habits;

public interface IHabitService
{
    /// <summary>Повний список (без видалених) або дельта від курсора (з видаленими). Зміна відміток змінює ServerModifiedAtUtc звички.</summary>
    Task<IReadOnlyList<HabitDto>> GetAsync(Guid userId, DateTime? modifiedSinceUtc, CancellationToken ct);

    Task<HabitDto> UpsertAsync(Guid userId, Guid id, UpsertHabitRequest request, CancellationToken ct);

    Task DeleteAsync(Guid userId, Guid id, CancellationToken ct);

    /// <summary>Ідемпотентна відмітка за день: повторний виклик нічого не змінює й не дублює подію.</summary>
    Task<HabitDto> PutLogAsync(Guid userId, Guid habitId, DateOnly date, DateTime? completedAtUtc, CancellationToken ct);

    /// <summary>Знімає відмітку за день (ідемпотентно).</summary>
    Task<HabitDto> DeleteLogAsync(Guid userId, Guid habitId, DateOnly date, CancellationToken ct);
}