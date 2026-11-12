using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Lighthouse.Api.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Lighthouse.Api.Infrastructure;

public static class RateLimitPolicies
{
    /// <summary>Політика для ендпоінтів автентифікації (реєстрація, вхід, оновлення токена).</summary>
    public const string Auth = "auth";
}

public static class RateLimitingExtensions
{
    public static IServiceCollection AddLighthouseRateLimiting(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RateLimitOptions>(configuration.GetSection(RateLimitOptions.SectionName));

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Загальний ліміт: на користувача (sub із JWT), а для неавтентифікованих — на IP-адресу. /health не обмежується.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                if (context.Request.Path.StartsWithSegments("/health"))
                {
                    return RateLimitPartition.GetNoLimiter("health");
                }

                var limits = context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;
                var userId = context.User.FindFirst(TokenClaims.Subject)?.Value
                             ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var key = userId is null ? $"ip:{ClientKey(context)}" : $"user:{userId}";

                return RateLimitPartition.GetFixedWindowLimiter(
                    key, _ => Window(limits.ApiPermitLimit, limits.ApiWindowSeconds));
            });

            options.AddPolicy(RateLimitPolicies.Auth, context =>
            {
                var limits = context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(
                    $"auth:{ClientKey(context)}", _ => Window(limits.AuthPermitLimit, limits.AuthWindowSeconds));
            });

            options.OnRejected = async (context, ct) =>
            {
                var response = context.HttpContext.Response;
                response.StatusCode = StatusCodes.Status429TooManyRequests;

                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                await response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Too many requests. Try again later."
                }, ct);
            };
        });

        return services;
    }

    private static string ClientKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static FixedWindowRateLimiterOptions Window(int permitLimit, int windowSeconds) => new()
    {
        PermitLimit = Math.Max(1, permitLimit),
        Window = TimeSpan.FromSeconds(Math.Max(1, windowSeconds)),
        QueueLimit = 0,
        AutoReplenishment = true
    };
}