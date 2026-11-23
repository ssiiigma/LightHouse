using System.Collections.Concurrent;
using Lighthouse.Shared.Contracts.Calendar;

namespace Lighthouse.Api.Services.Calendar;

/// <summary>
/// Одностороння синхронізація «завдання з дедлайном → подія Google Calendar». За цикл виконується до 50 операцій;
/// решта лишається на наступний цикл. Для одного користувача синхронізації виконуються по черзі.
/// </summary>
public sealed class CalendarSyncService(
    ICalendarStore store, IGoogleCalendarClient google, ILogger<CalendarSyncService> logger) : ICalendarSyncService
{
    public const int MaxOperationsPerRun = 50;

    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Locks = new();

    private enum Outcome { Created, Updated, Rejected }

    public async Task<CalendarSyncResultDto> SyncAsync(Guid userId, CancellationToken ct)
    {
        var gate = Locks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            return await SyncCoreAsync(userId, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task DisconnectAsync(Guid userId, CancellationToken ct)
    {
        var gate = Locks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var connection = await store.GetConnectionAsync(userId, ct);
            if (connection is null)
            {
                return;
            }

            try
            {
                var token = await google.GetAccessTokenAsync(userId, connection.RefreshToken, ct);
                foreach (var link in await store.GetLinksAsync(userId, ct))
                {
                    try
                    {
                        await google.DeleteEventAsync(token, connection.CalendarId, link.EventId, ct);
                    }
                    catch (Exception ex) when (ex is CalendarTransientException or CalendarRejectedException)
                    {
                        logger.LogInformation("An event could not be removed during disconnect ({Type}).", ex.GetType().Name);
                    }
                }

                await google.RevokeAsync(connection.RefreshToken, ct);
            }
            catch (Exception ex) when (ex is CalendarAuthException or CalendarTransientException)
            {
                logger.LogInformation("Calendar events were not cleaned up on disconnect ({Type}).", ex.GetType().Name);
            }

            await store.RemoveConnectionAsync(userId, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<CalendarSyncResultDto> SyncCoreAsync(Guid userId, CancellationToken ct)
    {
        var connection = await store.GetConnectionAsync(userId, ct);
        if (connection is null)
        {
            return new CalendarSyncResultDto(0, 0, 0, 0);
        }

        string token;
        try
        {
            token = await google.GetAccessTokenAsync(userId, connection.RefreshToken, ct);
        }
        catch (CalendarAuthException)
        {
            // Користувач відкликав доступ (або термін дії токена в режимі Testing скінчився): календар відключається.
            logger.LogInformation("Google Calendar access was revoked for user {UserId}; disconnecting.", userId);
            await store.RemoveConnectionAsync(userId, ct);
            return new CalendarSyncResultDto(0, 0, 0, 0);
        }
        catch (CalendarTransientException ex)
        {
            logger.LogWarning("Calendar sync skipped: {Message}", ex.Message);
            return new CalendarSyncResultDto(0, 0, 0, 1);
        }

        var tasks = (await store.GetTasksAsync(userId, ct)).ToDictionary(t => t.Id);
        var links = (await store.GetLinksAsync(userId, ct)).ToDictionary(l => l.TaskId);
        int created = 0, updated = 0, deleted = 0, pending = 0, budget = MaxOperationsPerRun;

        try
        {
            foreach (var link in links.Values)
            {
                tasks.TryGetValue(link.TaskId, out var task);

                if (!HasEvent(task))
                {
                    if (budget == 0) { pending++; continue; }

                    try
                    {
                        await google.DeleteEventAsync(token, connection.CalendarId, link.EventId, ct);
                    }
                    catch (CalendarRejectedException ex)
                    {
                        logger.LogWarning("An event could not be deleted: {Message}", ex.Message);
                    }

                    await store.RemoveLinkAsync(link.TaskId, ct);
                    deleted++;
                    budget--;
                }
                else if (task!.ServerModifiedAtUtc > link.SyncedAtUtc)
                {
                    if (budget == 0) { pending++; continue; }

                    budget--;
                    switch (await PushAsync(token, connection, task, link, ct))
                    {
                        case Outcome.Updated: updated++; break;
                        case Outcome.Created: created++; break;
                    }
                }
            }

            foreach (var task in tasks.Values.Where(t => HasEvent(t) && !links.ContainsKey(t.Id)))
            {
                if (budget == 0) { pending++; continue; }

                budget--;
                if (await PushAsync(token, connection, task, null, ct) == Outcome.Created)
                {
                    created++;
                }
            }
        }
        catch (CalendarTransientException ex)
        {
            logger.LogWarning("Calendar sync interrupted: {Message}", ex.Message);
            pending++; // решту виконаємо в наступному циклі
        }

        return new CalendarSyncResultDto(created, updated, deleted, pending);
    }

    private async Task<Outcome> PushAsync(
        string token, CalendarConnectionInfo connection, CalendarTaskInfo task, CalendarLinkInfo? link, CancellationToken ct)
    {
        var data = CalendarEventMapper.ToEvent(task);
        try
        {
            if (link is not null && await google.UpdateEventAsync(token, connection.CalendarId, link.EventId, data, ct))
            {
                await store.UpsertLinkAsync(task.Id, link.EventId, task.ServerModifiedAtUtc, ct);
                return Outcome.Updated;
            }

            // Події немає (видалена в календарі) або це нове завдання: створюємо.
            var eventId = await google.CreateEventAsync(token, connection.CalendarId, data, ct);
            try
            {
                await store.UpsertLinkAsync(task.Id, eventId, task.ServerModifiedAtUtc, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Зв'язок не збережено: прибираємо щойно створену подію, щоб наступний цикл не створив дублікат.
                logger.LogError(ex, "The link for task {TaskId} could not be saved; the new event is removed.", task.Id);
                try
                {
                    await google.DeleteEventAsync(token, connection.CalendarId, eventId, CancellationToken.None);
                }
                catch (Exception)
                {
                    // подію прибере ідемпотентне створення в наступному циклі
                }

                throw;
            }

            return Outcome.Created;
        }
        catch (CalendarRejectedException ex)
        {
            logger.LogWarning("Google rejected the event of task {TaskId}: {Message}", task.Id, ex.Message);
            return Outcome.Rejected; // це завдання пропускаємо, решта синхронізується
        }
    }

    private static bool HasEvent(CalendarTaskInfo? task) =>
        task is { IsDeleted: false, IsCompleted: false, DeadlineUtc: not null };
}