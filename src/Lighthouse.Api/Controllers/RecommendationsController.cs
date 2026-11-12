using FluentValidation;
using Lighthouse.Api.Infrastructure;
using Lighthouse.Api.Services.Recommendations;
using Lighthouse.Shared.Contracts.Recommendations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lighthouse.Api.Controllers;

[Route("api/recommendations")]
[Authorize]
public sealed class RecommendationsController(IRecommendationService recommendations) : ApiControllerBase
{
    /// <summary>Ранжування відкритих завдань користувача: перелік, пояснення, варіанти вирішення, ключі рекомендацій.</summary>
    [HttpGet("ranking")]
    public async Task<ActionResult<RankingResponse>> Ranking(CancellationToken ct) =>
        Ok(await recommendations.GetRankingAsync(User.GetUserId(), ct));

    [HttpPost("feedback")]
    public async Task<ActionResult<FeedbackResponse>> Feedback(
        FeedbackRequest request, [FromServices] IValidator<FeedbackRequest> validator, CancellationToken ct)
    {
        if (await ValidateAsync(validator, request, ct) is { } invalid) return invalid;
        return Ok(await recommendations.SubmitFeedbackAsync(User.GetUserId(), request, ct));
    }
}