using Lighthouse.Api.Data;
using Lighthouse.Api.Infrastructure;
using Lighthouse.Core.Entities;
using Lighthouse.Shared.Contracts.Categories;
using Microsoft.EntityFrameworkCore;

namespace Lighthouse.Api.Services.Categories;

public sealed class CategoryService(LighthouseDbContext db) : ICategoryService
{
    public async Task<IReadOnlyList<CategoryDto>> GetAsync(Guid userId, DateTime? modifiedSinceUtc, CancellationToken ct)
    {
        var query = db.Categories.AsNoTracking().Where(c => c.UserId == userId);

        query = SyncTime.ToUtc(modifiedSinceUtc) is { } since
            ? query.Where(c => c.ServerModifiedAtUtc > since)
            : query.Where(c => !c.IsDeleted);

        var items = await query.OrderBy(c => c.ServerModifiedAtUtc).ToListAsync(ct);
        return items.Select(ToDto).ToList();
    }

    public async Task<CategoryDto> UpsertAsync(Guid userId, Guid id, UpsertCategoryRequest request, CancellationToken ct)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (category is not null && category.UserId != userId)
        {
            throw new AppException(StatusCodes.Status404NotFound, "Category not found.");
        }

        var now = DateTime.UtcNow;
        var editedAt = SyncTime.ClampToNow(request.ClientUpdatedAtUtc, now);

        if (category is null)
        {
            category = new Category { Id = id, UserId = userId, CreatedAtUtc = now };
            db.Categories.Add(category);
        }
        else if (category.UpdatedAtUtc > editedAt)
        {
            return ToDto(category); // «останній запис»
        }

        category.Name = request.Name.Trim();
        category.ColorHex = request.ColorHex;
        category.Icon = request.Icon;
        category.IsDeleted = false;
        category.UpdatedAtUtc = editedAt;
        category.ServerModifiedAtUtc = now;

        await db.SaveChangesAsync(ct);
        return ToDto(category);
    }

    public async Task DeleteAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (category is null)
        {
            return; // ідемпотентно
        }

        if (category.UserId != userId)
        {
            throw new AppException(StatusCodes.Status404NotFound, "Category not found.");
        }

        if (category.IsDeleted)
        {
            return;
        }

        var now = DateTime.UtcNow;
        category.IsDeleted = true;
        category.UpdatedAtUtc = now;
        category.ServerModifiedAtUtc = now;

        // FK з NoAction (див. DbContext): відв'язуємо залежні записи вручну, щоб вони потрапили в дельту синхронізації.
        var tasks = await db.Tasks.Where(t => t.UserId == userId && t.CategoryId == id).ToListAsync(ct);
        foreach (var task in tasks)
        {
            task.CategoryId = null;
            task.UpdatedAtUtc = now;
            task.ServerModifiedAtUtc = now;
        }

        var habits = await db.Habits.Where(h => h.UserId == userId && h.CategoryId == id).ToListAsync(ct);
        foreach (var habit in habits)
        {
            habit.CategoryId = null;
            habit.UpdatedAtUtc = now;
            habit.ServerModifiedAtUtc = now;
        }

        await db.SaveChangesAsync(ct);
    }

    private static CategoryDto ToDto(Category c) => new(
        c.Id, c.Name, c.ColorHex, c.Icon, c.UpdatedAtUtc, c.ServerModifiedAtUtc, c.IsDeleted);
}