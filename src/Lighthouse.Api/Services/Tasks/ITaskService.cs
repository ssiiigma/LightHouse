using Lighthouse.Shared.Contracts.Tasks;

namespace Lighthouse.Api.Services.Tasks;

public interface ITaskService
{
    /// <summary>Повний список (без видалених) або дельта від курсора <paramref name="modifiedSinceUtc"/> (з видаленими).</summary>
    Task<IReadOnlyList<TaskDto>> GetAsync(Guid userId, DateTime? modifiedSinceUtc, CancellationToken ct);

    Task<TaskDto> UpsertAsync(Guid userId, Guid id, UpsertTaskRequest request, CancellationToken ct);

    Task DeleteAsync(Guid userId, Guid id, CancellationToken ct);
}
