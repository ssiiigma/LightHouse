using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Lighthouse.Api.Controllers;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>Повертає null, якщо модель коректна, інакше — готову відповідь 400 з переліком помилок.</summary>
    protected async Task<ActionResult?> ValidateAsync<T>(IValidator<T> validator, T model, CancellationToken ct)
    {
        var result = await validator.ValidateAsync(model, ct);
        return result.IsValid
            ? null
            : ValidationProblem(new ValidationProblemDetails(result.ToDictionary()));
    }
}
