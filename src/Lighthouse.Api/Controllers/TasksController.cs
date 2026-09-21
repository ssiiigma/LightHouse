using FluentValidation;
using Lighthouse.Api.Infrastructure;
using Lighthouse.Api.Services.Tasks;
using Lighthouse.Shared.Contracts.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lighthouse.Api.Controllers;

[Route("api/tasks")]
[Authorize]
public sealed class TasksController(ITaskService tasks) : ApiControllerBase
{
    /// <summary>Без параметра — повний список; з modifiedSince — дельта змін (для синхронізації).</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TaskDto>>> Get([FromQuery] DateTime? modifiedSince, CancellationToken ct) =>
        Ok(await tasks.GetAsync(User.GetUserId(), modifiedSince, ct));

    /// <summary>Створення або оновлення завдання; Id генерує клієнт.</summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TaskDto>> Upsert(
        Guid id, UpsertTaskRequest request, [FromServices] IValidator<UpsertTaskRequest> validator, CancellationToken ct)
    {
        if (await ValidateAsync(validator, request, ct) is { } invalid) return invalid;
        return Ok(await tasks.UpsertAsync(User.GetUserId(), id, request, ct));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await tasks.DeleteAsync(User.GetUserId(), id, ct);
        return NoContent();
    }
}
