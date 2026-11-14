using System.Globalization;
using System.Text.Json;
using Lighthouse.Mobile.Data;
using Lighthouse.Mobile.Infrastructure;
using Lighthouse.Mobile.Services.Api;
using Lighthouse.Shared.Contracts.Categories;
using Lighthouse.Shared.Sync;
using SQLite;

namespace Lighthouse.Mobile.Services.Sync;

public sealed class CategorySyncHandler(IApiClient api, ILocalDB localDb) : IEntitySyncHandler
{
    private const int CursorOverlapSeconds = 5;

    public string EntityType => EntityTypes.Category;

    public int PullOrder => 10;

    public async Task PushAsync(OutboxItem item, CancellationToken ct)
    {
        var db = await localDb.GetConnectionAsync();

        if (item.Operation == OutboxStore.Delete)
        {
            await api.DeleteAsync($"api/categories/{item.EntityId}", ct);
            await db.RunInTransactionAsync(tx =>
            {
                var row = tx.Find<LocalCategory>(item.EntityId);
                if (row is { IsDeleted: true } && !OutboxStore.HasOtherPending(tx, item))
                {
                    tx.Delete<LocalCategory>(item.EntityId);
                }
            });
            return;
        }

        var request = JsonSerializer.Deserialize<UpsertCategoryRequest>(item.PayloadJson!, AppJson.Options)!;
        var dto = await api.PutAsync<UpsertCategoryRequest, CategoryDto>($"api/categories/{item.EntityId}", request, ct);
        await db.RunInTransactionAsync(tx =>
        {
            var row = tx.Find<LocalCategory>(dto.Id);
            if (row is null || OutboxStore.HasOtherPending(tx, item))
            {
                return;
            }

            if (dto.UpdatedAtUtc > Utc.Ensure(row.UpdatedAtUtc))
            {
                ApplyRemote(tx, dto, force: true);
                return;
            }

            row.SyncState = SyncState.Synced;
            row.ServerModifiedAtUtc = dto.ServerModifiedAtUtc;
            tx.Update(row);
        });
    }

    public async Task<int> PullAsync(CancellationToken ct)
    {
        var cursor = SyncCursor.Load(EntityType);
        var path = cursor is { } since
            ? "api/categories?modifiedSince=" + Uri.EscapeDataString(
                since.AddSeconds(-CursorOverlapSeconds).ToString("O", CultureInfo.InvariantCulture))
            : "api/categories";

        var items = await api.GetAsync<List<CategoryDto>>(path, ct);
        if (items.Count == 0)
        {
            return 0;
        }

        var db = await localDb.GetConnectionAsync();
        var applied = 0;
        await db.RunInTransactionAsync(tx =>
        {
            foreach (var dto in items)
            {
                if (ApplyRemote(tx, dto, force: false))
                {
                    applied++;
                }
            }
        });

        SyncCursor.Save(EntityType, items.Max(i => i.ServerModifiedAtUtc));
        return applied;
    }

    public async Task MarkFailedAsync(OutboxItem item)
    {
        var db = await localDb.GetConnectionAsync();
        await db.RunInTransactionAsync(tx =>
        {
            if (OutboxStore.HasOtherPending(tx, item))
            {
                return;
            }

            if (tx.Find<LocalCategory>(item.EntityId) is { } row)
            {
                row.SyncState = SyncState.Failed;
                tx.Update(row);
            }
        });
    }

    private static bool ApplyRemote(SQLiteConnection tx, CategoryDto dto, bool force)
    {
        var local = tx.Find<LocalCategory>(dto.Id);

        if (!force && local is not null)
        {
            if (local.SyncState != SyncState.Synced
                && !SyncRules.ShouldApplyRemote(Utc.Ensure(local.UpdatedAtUtc), dto.UpdatedAtUtc))
            {
                return false;
            }

            if (local.SyncState == SyncState.Synced && !dto.IsDeleted
                && Utc.Ensure(local.ServerModifiedAtUtc) >= dto.ServerModifiedAtUtc)
            {
                return false;
            }
        }

        tx.Table<OutboxItem>().Delete(i => i.EntityType == EntityTypes.Category && i.EntityId == dto.Id);

        if (dto.IsDeleted)
        {
            tx.Delete<LocalCategory>(dto.Id);
            return local is not null;
        }

        tx.InsertOrReplace(new LocalCategory
        {
            Id = dto.Id,
            Name = dto.Name,
            ColorHex = dto.ColorHex,
            Icon = dto.Icon,
            UpdatedAtUtc = dto.UpdatedAtUtc,
            ServerModifiedAtUtc = dto.ServerModifiedAtUtc,
            IsDeleted = false,
            SyncState = SyncState.Synced
        });
        return true;
    }
}