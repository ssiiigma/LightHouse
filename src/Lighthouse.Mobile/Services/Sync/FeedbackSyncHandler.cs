using System.Net;
using System.Text.Json;
using Lighthouse.Mobile.Data;
using Lighthouse.Mobile.Infrastructure;
using Lighthouse.Mobile.Services.Api;
using Lighthouse.Shared.Contracts.Recommendations;

namespace Lighthouse.Mobile.Services.Sync;

/// <summary>Відгуки на рекомендації (приймаю/відхиляю) надсилаються з черги; ключ, якого сервер не знає, відкидається.</summary>
public sealed class FeedbackSyncHandler(IApiClient api) : IEntitySyncHandler
{
    public string EntityType => EntityTypes.Feedback;

    public int PullOrder => 40;

    public async Task PushAsync(OutboxItem item, CancellationToken ct)
    {
        var request = JsonSerializer.Deserialize<FeedbackRequest>(item.PayloadJson!, AppJson.Options)!;
        try
        {
            await api.PostAsync<FeedbackRequest, FeedbackResponse>("api/recommendations/feedback", request, ct);
        }
        catch (ApiException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            // Ключ застарів (сервер зберігає неопрацьовані рекомендації 14 діб): відгук втратив сенс.
        }
    }

    public Task<int> PullAsync(CancellationToken ct) => Task.FromResult(0);

    public Task MarkFailedAsync(OutboxItem item) => Task.CompletedTask;
}