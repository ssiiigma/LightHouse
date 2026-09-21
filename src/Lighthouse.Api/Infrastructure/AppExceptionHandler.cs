using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Lighthouse.Api.Infrastructure;

public sealed class AppExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        if (exception is not AppException appException)
        {
            return false; // непередбачені помилки обробляє стандартний обробник (500)
        }

        httpContext.Response.StatusCode = appException.StatusCode;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = appException.StatusCode,
                Title = appException.Message
            }
        });
    }
}
