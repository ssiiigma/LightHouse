namespace Lighthouse.Api.Infrastructure;

/// <summary>Очікувана помилка бізнес-логіки, що перетворюється на ProblemDetails із заданим HTTP-статусом.</summary>
public sealed class AppException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
