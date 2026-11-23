using Lighthouse.Api.Infrastructure;
using Lighthouse.Api.Options;
using Lighthouse.Api.Services.Calendar;
using Lighthouse.Shared.Contracts.Calendar;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Lighthouse.Api.Controllers;

[Route("api/calendar")]
public sealed class CalendarController(
    ICalendarStore store, ICalendarSyncService sync, IGoogleCalendarClient google, CalendarStateProtector states,
    IOptions<AppOptions> app, IServiceScopeFactory scopes, ILogger<CalendarController> logger, 
    IWebHostEnvironment environment) : ApiControllerBase
{
    private string RedirectUri => $"{app.Value.PublicBaseUrl.TrimEnd('/')}/api/calendar/callback";

    [HttpGet("status")]
    [Authorize]
    public async Task<ActionResult<CalendarStatusDto>> Status(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var connected = await store.IsConnectedAsync(userId, ct);
        return Ok(new CalendarStatusDto(connected, connected ? (await store.GetLinksAsync(userId, ct)).Count : 0));
    }

    /// <summary>Адреса входу Google для підключення календаря (відкривається в браузері).</summary>
    [HttpPost("connect")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public ActionResult<CalendarConnectDto> Connect() =>
        Ok(new CalendarConnectDto(google.BuildAuthorizationUrl(states.Create(User.GetUserId()), RedirectUri)));

    /// <summary>Повернення з Google: обмін коду на refresh-токен (його користувач визначається зі state).</summary>
    [HttpGet("callback")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state)
            || states.Read(state) is not { } userId)
        {
            return Page(false);
        }

        try
        {
            var tokens = await google.ExchangeCodeAsync(code, RedirectUri, ct);
            await store.SaveConnectionAsync(userId, tokens.RefreshToken, "primary", ct);
        }
        catch (Exception ex) when (ex is CalendarAuthException or CalendarTransientException or CalendarRejectedException)
        {
            logger.LogWarning("The calendar could not be connected: {Message}", ex.Message);
            return Page(false);
        }

        _ = Task.Run(() => InitialSyncAsync(userId));
        return Page(true);
    }

    [HttpPost("sync")]
    [Authorize]
    public async Task<ActionResult<CalendarSyncResultDto>> SyncNow(CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (!await store.IsConnectedAsync(userId, ct))
        {
            throw new AppException(StatusCodes.Status409Conflict, "The calendar is not connected.");
        }

        try
        {
            return Ok(await sync.SyncAsync(userId, ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not AppException)
        {
            logger.LogError(ex, "Calendar sync failed for user {UserId}.", userId);

            // У розробці причину видно у відповіді; у виробничому режимі деталі не розкриваються.
            if (environment.IsDevelopment())
            {
                return Problem(title: "Calendar sync failed", detail: ex.GetBaseException().Message, statusCode: 500);
            }

            throw;
        }
    }

    /// <summary>Відключення: події, створені Lighthouse, видаляються з календаря, токен відкликається.</summary>
    [HttpDelete]
    [Authorize]
    public async Task<IActionResult> Disconnect(CancellationToken ct)
    {
        await sync.DisconnectAsync(User.GetUserId(), ct);
        return NoContent();
    }

    private async Task InitialSyncAsync(Guid userId)
    {
        try
        {
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ICalendarSyncService>().SyncAsync(userId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The initial calendar sync failed for user {UserId}.", userId);
        }
    }

    private ContentResult Page(bool ok) => new()
    {
        Content = AccountPages.CalendarResult(ok),
        ContentType = "text/html; charset=utf-8",
        StatusCode = ok ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest
    };
}