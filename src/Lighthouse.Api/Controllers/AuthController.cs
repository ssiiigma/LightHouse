using FluentValidation;
using Lighthouse.Api.Infrastructure;
using Lighthouse.Api.Services.Auth;
using Lighthouse.Shared.Contracts.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Lighthouse.Api.Controllers;

[Route("api/auth")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Auth)]
public sealed class AuthController(IAuthService auth, IAccountService accounts) : ApiControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(
        RegisterRequest request, [FromServices] IValidator<RegisterRequest> validator, CancellationToken ct)
    {
        if (await ValidateAsync(validator, request, ct) is { } invalid) return invalid;
        return Ok(await auth.RegisterAsync(request, ct));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(
        LoginRequest request, [FromServices] IValidator<LoginRequest> validator, CancellationToken ct)
    {
        if (await ValidateAsync(validator, request, ct) is { } invalid) return invalid;
        return Ok(await auth.LoginAsync(request, ct));
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponse>> Refresh(
        RefreshRequest request, [FromServices] IValidator<RefreshRequest> validator, CancellationToken ct)
    {
        if (await ValidateAsync(validator, request, ct) is { } invalid) return invalid;
        return Ok(await auth.RefreshAsync(request.RefreshToken, ct));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(LogoutRequest request, CancellationToken ct)
    {
        await auth.LogoutAsync(request.RefreshToken, ct);
        return NoContent();
    }

    /// <summary>Лист зі скиданням пароля. Відповідь 202 однакова для існуючих і неіснуючих адрес.</summary>
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(
        ForgotPasswordRequest request, [FromServices] IValidator<ForgotPasswordRequest> validator, CancellationToken ct)
    {
        if (await ValidateAsync(validator, request, ct) is { } invalid) return invalid;
        await accounts.RequestPasswordResetAsync(request.Email, request.Language, ct);
        return Accepted();
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(
        ResetPasswordRequest request, [FromServices] IValidator<ResetPasswordRequest> validator, CancellationToken ct)
    {
        if (await ValidateAsync(validator, request, ct) is { } invalid) return invalid;
        await accounts.ResetPasswordAsync(request.Token, request.NewPassword, ct);
        return NoContent();
    }

    [HttpPost("confirm-email")]
    public async Task<IActionResult> ConfirmEmail(
        ConfirmEmailRequest request, [FromServices] IValidator<ConfirmEmailRequest> validator, CancellationToken ct)
    {
        if (await ValidateAsync(validator, request, ct) is { } invalid) return invalid;
        await accounts.ConfirmEmailAsync(request.Token, ct);
        return NoContent();
    }

    /// <summary>Посилання з листа підтвердження: показує сторінку з результатом.</summary>
    [HttpGet("confirm-email")]
    public async Task<IActionResult> ConfirmEmailPage([FromQuery] string? token, CancellationToken ct)
    {
        try
        {
            await accounts.ConfirmEmailAsync(token ?? string.Empty, ct);
            return Content(AccountPages.ConfirmResult(true), "text/html; charset=utf-8");
        }
        catch (AppException)
        {
            return new ContentResult
            {
                Content = AccountPages.ConfirmResult(false),
                ContentType = "text/html; charset=utf-8",
                StatusCode = StatusCodes.Status400BadRequest
            };
        }
    }

    /// <summary>Вхід через Google: ID-токен перевіряється сервером; акаунт прив'язується за Google-ідентифікатором або e-mail.</summary>
    [HttpPost("google")]
    public async Task<ActionResult<AuthResponse>> Google(
        GoogleLoginRequest request, [FromServices] IValidator<GoogleLoginRequest> validator, CancellationToken ct)
    {
        if (await ValidateAsync(validator, request, ct) is { } invalid) return invalid;
        return Ok(await auth.GoogleLoginAsync(request, ct));
    }
}