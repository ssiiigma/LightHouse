using System.Collections.Concurrent;

namespace Lighthouse.Api.Services.Calendar;

/// <summary>
/// Запускає синхронізацію календаря користувача через кілька секунд після зміни його завдань (серія змін зливається в одну),
/// щоб подія з'являлась швидше, ніж спрацює фоновий цикл.
/// </summary>
public sealed class CalendarTrigger(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<CalendarTrigger> logger)
{
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(3);

    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _pending = new();
    private readonly bool _enabled = configuration.GetValue("Workers:CalendarEnabled", true);

    public void Notify(Guid userId)
    {
        if (!_enabled)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        if (_pending.TryGetValue(userId, out var previous))
        {
            previous.Cancel();
        }

        _pending[userId] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(Delay, cts.Token);

                using var scope = scopes.CreateScope();
                if (!await scope.ServiceProvider.GetRequiredService<ICalendarStore>().IsConnectedAsync(userId, cts.Token))
                {
                    return; // календар не підключено
                }

                await scope.ServiceProvider.GetRequiredService<ICalendarSyncService>().SyncAsync(userId, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // замінено новішою зміною
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Triggered calendar sync failed for user {UserId}.", userId);
            }
            finally
            {
                _pending.TryRemove(new KeyValuePair<Guid, CancellationTokenSource>(userId, cts));
            }
        });
    }
}