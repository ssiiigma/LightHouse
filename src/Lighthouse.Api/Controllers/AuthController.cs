using FluentValidation;
using Lighthouse.Api.Services.Auth;
using Lighthouse.Shared.Contracts.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Lighthouse.Api.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;

namespace Lighthouse.Api.Controllers;

[Route("api/auth")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Auth)]
public sealed class AuthController(IAuthService auth) : ApiControllerBase
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

    // TODO: POST /api/auth/google (GoogleLoginRequest) — перевірка ID-токена через Google.Apis.Auth.
}
