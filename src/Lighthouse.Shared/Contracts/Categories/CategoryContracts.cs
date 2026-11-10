namespace Lighthouse.Shared.Contracts.Categories;

public sealed record CategoryDto(
    Guid Id,
    string Name,
    string? ColorHex,
    string? Icon,
    DateTime UpdatedAtUtc,
    DateTime ServerModifiedAtUtc,
    bool IsDeleted);

/// <summary>Тіло PUT /api/categories/{id} (upsert, Id генерує клієнт).</summary>
public sealed record UpsertCategoryRequest(
    string Name,
    string? ColorHex,
    string? Icon,
    DateTime ClientUpdatedAtUtc);