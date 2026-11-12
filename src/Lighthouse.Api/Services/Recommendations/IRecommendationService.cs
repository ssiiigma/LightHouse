using Lighthouse.Shared.Contracts.Recommendations;

namespace Lighthouse.Api.Services.Recommendations;

public interface IRecommendationService
{
    /// <summary>Ранжований перелік відкритих завдань користувача (список бере сервер зі своєї БД).</summary>
    Task<RankingResponse> GetRankingAsync(Guid userId, CancellationToken ct);

    /// <summary>Відгук на рекомендацію; повторний відгук із тим самим ключем ігнорується (Applied = false).</summary>
    Task<FeedbackResponse> SubmitFeedbackAsync(Guid userId, FeedbackRequest request, CancellationToken ct);
}