using FluentValidation;
using Lighthouse.Api.Infrastructure;
using Lighthouse.Api.Services.Notifications;
using Lighthouse.Shared.Contracts.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lighthouse.Api.Controllers;

[Route("api/devices")]
[Authorize]
public sealed class DevicesController(IDeviceService devices) : ApiControllerBase
{
    /// <summary>Реєструє токен FCM поточного пристрою (повторна реєстрація безпечна).</summary>
    [HttpPost]
    public async Task<IActionResult> Register(
        RegisterDeviceRequest request, [FromServices] IValidator<RegisterDeviceRequest> validator, CancellationToken ct)
    {
        if (await ValidateAsync(validator, request, ct) is { } invalid) return invalid;
        await devices.RegisterAsync(User.GetUserId(), request.FcmToken, ct);
        return NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> Unregister([FromQuery] string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 512)
        {
            throw new AppException(StatusCodes.Status400BadRequest, "token is required.");
        }

        await devices.UnregisterAsync(User.GetUserId(), token, ct);
        return NoContent();
    }
}