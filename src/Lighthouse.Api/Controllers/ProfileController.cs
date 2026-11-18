using FluentValidation;
using Lighthouse.Api.Data;
using Lighthouse.Api.Infrastructure;
using Lighthouse.Shared.Contracts.Profile;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lighthouse.Api.Controllers;

[Route("api/profile")]
[Authorize]
public sealed class ProfileController(LighthouseDbContext db) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ProfileDto>> Get(CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == User.GetUserId() && !u.IsDeleted, ct)
                   ?? throw new AppException(StatusCodes.Status404NotFound, "User not found.");
        return Ok(ToDto(user));
    }

    [HttpPut]
    public async Task<ActionResult<ProfileDto>> Update(
        UpdateProfileRequest request, [FromServices] IValidator<UpdateProfileRequest> validator, CancellationToken ct)
    {
        if (await ValidateAsync(validator, request, ct) is { } invalid) return invalid;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == User.GetUserId() && !u.IsDeleted, ct)
                   ?? throw new AppException(StatusCodes.Status404NotFound, "User not found.");

        var now = DateTime.UtcNow;
        user.Name = request.Name.Trim();
        user.UpdatedAtUtc = now;
        user.ServerModifiedAtUtc = now;
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(user));
    }

    [HttpPut("timezone")]
    public async Task<ActionResult<TimeZoneDto>> SetTimeZone(TimeZoneDto request, CancellationToken ct)
    {
        var id = request.TimeZoneId?.Trim();
        if (string.IsNullOrEmpty(id) || id.Length > 64 || !TimeZoneInfo.TryFindSystemTimeZoneById(id, out _))
        {
            throw new AppException(StatusCodes.Status400BadRequest, "Unknown time zone.");
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == User.GetUserId() && !u.IsDeleted, ct)
                   ?? throw new AppException(StatusCodes.Status404NotFound, "User not found.");

        user.TimeZoneId = id;
        user.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new TimeZoneDto(id));
    }

    private static ProfileDto ToDto(Core.Entities.User user) =>
        new(user.Id, user.Name, user.Email, user.TimeZoneId, user.IsEmailConfirmed);
}