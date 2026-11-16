using Lighthouse.Mobile.Infrastructure;
using SQLite;

namespace Lighthouse.Mobile.Services.Sync;

/// <summary>Завантаження частково вдалося: застосовано <see cref="Applied"/> записів, інші відкладено до наступної спроби.</summary>
public sealed class SyncPullException(string message, int applied) : Exception(message)
{
    public int Applied { get; } = applied;
}

internal static class PullHelper
{
    /// <summary>
    /// Застосовує кожен запис сервера в окремій транзакції. Помилка одного запису не зупиняє решту; курсор просувається
    /// лише до останнього успішного запису перед першим збоєм (збійний запис буде отримано знову).
    /// </summary>
    public static async Task<int> ApplyAsync<TDto>(
        SQLiteAsyncConnection db, string entityType, IReadOnlyList<TDto> items,
        Func<SQLiteConnection, TDto, bool> apply, Func<TDto, DateTime> serverModified)
    {
        var applied = 0;
        var failed = false;
        string? firstError = null;
        DateTime? safeCursor = null;

        foreach (var dto in items.OrderBy(serverModified))
        {
            try
            {
                var changed = false;
                await db.RunInTransactionAsync(tx => changed = apply(tx, dto));
                if (changed)
                {
                    applied++;
                }

                if (!failed)
                {
                    safeCursor = serverModified(dto);
                }
            }
            catch (Exception ex)
            {
                failed = true;
                firstError ??= $"{ex.GetType().Name}: {ex.Message}";
            }
        }

        if (safeCursor is { } cursor)
        {
            await db.RunInTransactionAsync(tx => SyncCursor.Save(tx, entityType, cursor));
        }

        if (firstError is not null)
        {
            var text = firstError.Length <= 140 ? firstError : firstError[..140];
            throw new SyncPullException($"{entityType}: {text}", applied);
        }

        return applied;
    }
}