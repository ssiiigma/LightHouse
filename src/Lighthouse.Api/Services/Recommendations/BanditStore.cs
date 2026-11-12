using System.Text.Json;
using Lighthouse.Api.Data;
using Lighthouse.Core.AI;
using Lighthouse.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lighthouse.Api.Services.Recommendations;

public sealed record LoadedModel(LinUcbModel Model, BanditState? State);

/// <summary>
/// Зберігання моделі користувача (знімок BanditState з версією). Запис проводиться з перевіркою версії:
/// Version є токеном конкурентності, тож конкурентний запис завершується DbUpdateConcurrencyException.
/// </summary>
public sealed class BanditStore(LighthouseDbContext db)
{
    public async Task<LoadedModel> LoadAsync(Guid userId, CancellationToken ct)
    {
        var state = await db.BanditStates.FirstOrDefaultAsync(s => s.UserId == userId, ct);
        if (state is null)
        {
            return new LoadedModel(NewModel(), null);
        }

        var snapshot = JsonSerializer.Deserialize<LinUcbSnapshot>(state.SnapshotJson);
        var model = snapshot is { Dimension: LinUcbDefaults.Dimension }
            ? LinUcbModel.FromSnapshot(snapshot)
            : NewModel(); // розмірність змінилась між версіями — починаємо з чистої моделі

        return new LoadedModel(model, state);
    }

    /// <summary>Готує запис знімка (без SaveChanges) і повертає нову версію.</summary>
    public int Stage(Guid userId, LoadedModel loaded, DateTime nowUtc)
    {
        var json = JsonSerializer.Serialize(loaded.Model.ToSnapshot());

        if (loaded.State is null)
        {
            db.BanditStates.Add(new BanditState
            {
                UserId = userId,
                Dimension = loaded.Model.Dimension,
                SnapshotJson = json,
                Version = 1,
                UpdatedAtUtc = nowUtc
            });
            return 1;
        }

        loaded.State.Dimension = loaded.Model.Dimension;
        loaded.State.SnapshotJson = json;
        loaded.State.Version++;
        loaded.State.UpdatedAtUtc = nowUtc;
        return loaded.State.Version;
    }

    public Task<int> CountFeedbackAsync(Guid userId, CancellationToken ct) =>
        db.RecommendationFeedbacks.CountAsync(f => f.UserId == userId && f.Reward != null, ct);

    private static LinUcbModel NewModel() => new(LinUcbDefaults.Dimension, LinUcbDefaults.Alpha);
}