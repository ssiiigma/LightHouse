using System.Globalization;
using FluentValidation;
using Lighthouse.Api.Infrastructure;
using Lighthouse.Api.Services.Habits;
using Lighthouse.Shared.Contracts.Habits;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lighthouse.Api.Controllers;

[Route("api/habits")]
[Authorize]
public sealed class HabitsController(IHabitService habits) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<HabitDto>>> Get([FromQuery] DateTime? modifiedSince, CancellationToken ct) =>
        Ok(await habits.GetAsync(User.GetUserId(), modifiedSince, ct));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<HabitDto>> Upsert(
        Guid id, UpsertHabitRequest request, [FromServices] IValidator<UpsertHabitRequest> validator, CancellationToken ct)
    {
        if (await ValidateAsync(validator, request, ct) is { } invalid) return invalid;
        return Ok(await habits.UpsertAsync(User.GetUserId(), id, request, ct));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await habits.DeleteAsync(User.GetUserId(), id, ct);
        return NoContent();
    }

    /// <summary>Відмітка виконання за день (yyyy-MM-dd); тіло необов'язкове.</summary>
    [HttpPut("{id:guid}/logs/{date}")]
    public async Task<ActionResult<HabitDto>> PutLog(
        Guid id, string date, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] UpsertHabitLogRequest? request, CancellationToken ct) =>
        Ok(await habits.PutLogAsync(User.GetUserId(), id, ParseDate(date), request?.CompletedAtUtc, ct));

    [HttpDelete("{id:guid}/logs/{date}")]
    public async Task<ActionResult<HabitDto>> DeleteLog(Guid id, string date, CancellationToken ct) =>
        Ok(await habits.DeleteLogAsync(User.GetUserId(), id, ParseDate(date), ct));

    private static DateOnly ParseDate(string value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new AppException(StatusCodes.Status400BadRequest, "Date must be in yyyy-MM-dd format.");
}