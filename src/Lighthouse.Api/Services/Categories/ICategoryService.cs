using Lighthouse.Shared.Contracts.Categories;

namespace Lighthouse.Api.Services.Categories;

public interface ICategoryService
{
    /// <summary>Повний список (без видалених) або дельта від курсора (з видаленими).</summary>
    Task<IReadOnlyList<CategoryDto>> GetAsync(Guid userId, DateTime? modifiedSinceUtc, CancellationToken ct);

    Task<CategoryDto> UpsertAsync(Guid userId, Guid id, UpsertCategoryRequest request, CancellationToken ct);

    /// <summary>М'яке видалення; завдання й звички цієї категорії переносяться в «Без категорії».</summary>
    Task DeleteAsync(Guid userId, Guid id, CancellationToken ct);
}