using FluentValidation;
using Lighthouse.Api.Infrastructure;
using Lighthouse.Api.Services.Notifications;
using Lighthouse.Shared.Contracts.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lighthouse.Api.Controllers;

[Route("api/notification-settings")]
[Authorize]
public sealed class NotificationSettingsController(INotificationSettingsService settings) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<NotificationSettingsDto>> Get(CancellationToken ct) =>
        Ok(await settings.GetAsync(User.GetUserId(), ct));

    [HttpPut]
    public async Task<ActionResult<NotificationSettingsDto>> Put(
        NotificationSettingsDto request, [FromServices] IValidator<NotificationSettingsDto> validator, CancellationToken ct)
    {
        if (await ValidateAsync(validator, request, ct) is { } invalid) return invalid;
        return Ok(await settings.UpdateAsync(User.GetUserId(), request, ct));
    }
}