using FluentValidation;
using Lighthouse.Api.Infrastructure;
using Lighthouse.Api.Services.Categories;
using Lighthouse.Shared.Contracts.Categories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lighthouse.Api.Controllers;

[Route("api/categories")]
[Authorize]
public sealed class CategoriesController(ICategoryService categories) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> Get([FromQuery] DateTime? modifiedSince, CancellationToken ct) =>
        Ok(await categories.GetAsync(User.GetUserId(), modifiedSince, ct));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CategoryDto>> Upsert(
        Guid id, UpsertCategoryRequest request, [FromServices] IValidator<UpsertCategoryRequest> validator, CancellationToken ct)
    {
        if (await ValidateAsync(validator, request, ct) is { } invalid) return invalid;
        return Ok(await categories.UpsertAsync(User.GetUserId(), id, request, ct));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await categories.DeleteAsync(User.GetUserId(), id, ct);
        return NoContent();
    }
}