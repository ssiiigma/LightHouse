using Lighthouse.Api.Services.Calendar;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lighthouse.Api.Tests;

internal sealed class FakeCalendarStore : ICalendarStore
{
    public CalendarConnectionInfo? Connection { get; set; }
    public Dictionary<Guid, CalendarTaskInfo> Tasks { get; } = new();
    public Dictionary<Guid, CalendarLinkInfo> Links { get; } = new();
    public bool FailUpsert { get; set; }

    public Task<bool> IsConnectedAsync(Guid userId, CancellationToken ct) => Task.FromResult(Connection is not null);
    public Task<CalendarConnectionInfo?> GetConnectionAsync(Guid userId, CancellationToken ct) => Task.FromResult(Connection);

    public Task SaveConnectionAsync(Guid userId, string refreshToken, string calendarId, CancellationToken ct)
    {
        Connection = new CalendarConnectionInfo(userId, refreshToken, calendarId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Guid>> ConnectedUserIdsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Guid>>(Connection is null ? [] : [Connection.UserId]);

    public Task<IReadOnlyList<CalendarTaskInfo>> GetTasksAsync(Guid userId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CalendarTaskInfo>>(Tasks.Values.ToList());

    public Task<IReadOnlyList<CalendarLinkInfo>> GetLinksAsync(Guid userId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CalendarLinkInfo>>(Links.Values.ToList());

    public Task UpsertLinkAsync(Guid taskId, string eventId, DateTime syncedAtUtc, CancellationToken ct)
    {
        if (FailUpsert) throw new InvalidOperationException("link storage failed");
        Links[taskId] = new CalendarLinkInfo(taskId, eventId, syncedAtUtc);
        return Task.CompletedTask;
    }

    public Task RemoveLinkAsync(Guid taskId, CancellationToken ct)
    {
        Links.Remove(taskId);
        return Task.CompletedTask;
    }

    public Task RemoveConnectionAsync(Guid userId, CancellationToken ct)
    {
        Connection = null;
        Links.Clear();
        return Task.CompletedTask;
    }
}

public class CalendarSyncServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime T0 = new(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc);

    private readonly FakeCalendarStore _store = new()
    {
        Connection = new CalendarConnectionInfo(UserId, "refresh", "primary")
    };

    private readonly FakeGoogleCalendarClient _google = new();

    private CalendarSyncService Service() =>
        new(_store, _google, NullLogger<CalendarSyncService>.Instance);

    private Guid AddTask(
        string title, DateTime? deadline = null, bool completed = false, bool deleted = false, DateTime? modified = null)
    {
        var id = Guid.NewGuid();
        _store.Tasks[id] = new CalendarTaskInfo(
            id, title, null, deadline, null, null, completed, deleted, modified ?? T0);
        return id;
    }

    private static DateTime Due(int days) => T0.AddDays(days);

    [Fact]
    public async Task Sync_CreatesEventsOnlyForOpenTasksWithDeadline()
    {
        AddTask("Open", Due(1));
        AddTask("No deadline");
        AddTask("Done", Due(1), completed: true);
        AddTask("Deleted", Due(1), deleted: true);

        var result = await Service().SyncAsync(UserId, default);

        Assert.Equal(1, result.Created);
        Assert.Equal("Open", Assert.Single(_google.Events.Values).Summary);
        Assert.Single(_store.Links);
    }

    [Fact]
    public async Task Sync_IsIdempotent()
    {
        AddTask("Open", Due(1));
        await Service().SyncAsync(UserId, default);
        var before = _google.Operations.Count;

        var second = await Service().SyncAsync(UserId, default);

        Assert.Equal(new Lighthouse.Shared.Contracts.Calendar.CalendarSyncResultDto(0, 0, 0, 0), second);
        Assert.Equal(before, _google.Operations.Count);
    }

    [Fact]
    public async Task Sync_UpdatesChangedTask_AndDeletesCompletedOrDeletedOnes()
    {
        var changed = AddTask("Old title", Due(1));
        var finished = AddTask("Will finish", Due(2));
        var removed = AddTask("Will vanish", Due(3));
        await Service().SyncAsync(UserId, default);

        _store.Tasks[changed] = _store.Tasks[changed] with { Title = "New title", ServerModifiedAtUtc = T0.AddHours(1) };
        _store.Tasks[finished] = _store.Tasks[finished] with { IsCompleted = true, ServerModifiedAtUtc = T0.AddHours(1) };
        _store.Tasks[removed] = _store.Tasks[removed] with { IsDeleted = true, ServerModifiedAtUtc = T0.AddHours(1) };

        var result = await Service().SyncAsync(UserId, default);

        Assert.Equal(1, result.Updated);
        Assert.Equal(2, result.Deleted);
        Assert.Equal("New title", Assert.Single(_google.Events.Values).Summary);
        Assert.Single(_store.Links);
    }

    [Fact]
    public async Task Sync_RemovesEventWhenDeadlineIsCleared()
    {
        var id = AddTask("With deadline", Due(1));
        await Service().SyncAsync(UserId, default);

        _store.Tasks[id] = _store.Tasks[id] with { DeadlineUtc = null, ServerModifiedAtUtc = T0.AddHours(1) };
        var result = await Service().SyncAsync(UserId, default);

        Assert.Equal(1, result.Deleted);
        Assert.Empty(_google.Events);
        Assert.Empty(_store.Links);
    }

    [Fact]
    public async Task Sync_RecreatesAnEventDeletedInTheCalendar()
    {
        var id = AddTask("Precious", Due(1));
        await Service().SyncAsync(UserId, default);
        var oldEvent = _store.Links[id].EventId;
        _google.Events.TryRemove(oldEvent, out _); // користувач видалив подію в календарі

        _store.Tasks[id] = _store.Tasks[id] with { Title = "Precious v2", ServerModifiedAtUtc = T0.AddHours(1) };
        var result = await Service().SyncAsync(UserId, default);

        Assert.Equal(1, result.Created);
        Assert.NotEqual(oldEvent, _store.Links[id].EventId);
        Assert.Equal("Precious v2", Assert.Single(_google.Events.Values).Summary);
    }

    [Fact]
    public async Task Sync_RespectsTheOperationBudget_AndFinishesInTheNextRun()
    {
        for (var i = 0; i < 60; i++)
        {
            AddTask($"Task {i}", Due(1));
        }

        var first = await Service().SyncAsync(UserId, default);
        var second = await Service().SyncAsync(UserId, default);

        Assert.Equal(CalendarSyncService.MaxOperationsPerRun, first.Created);
        Assert.Equal(10, first.Remaining);
        Assert.Equal(10, second.Created);
        Assert.Equal(0, second.Remaining);
        Assert.Equal(60, _google.Events.Count);
    }

    [Fact]
    public async Task Sync_DisconnectsWhenAccessWasRevoked()
    {
        AddTask("Open", Due(1));
        _google.InvalidGrant = true;

        var result = await Service().SyncAsync(UserId, default);

        Assert.Equal(0, result.Created);
        Assert.Null(_store.Connection);
        Assert.Empty(_google.Operations);
    }

    [Fact]
    public async Task Sync_StopsOnTransientErrors_AndResumesLater()
    {
        AddTask("Open", Due(1));
        _google.Transient = true;

        var interrupted = await Service().SyncAsync(UserId, default);
        Assert.Equal(0, interrupted.Created);
        Assert.True(interrupted.Remaining > 0);
        Assert.NotNull(_store.Connection);
        Assert.Empty(_store.Links);

        _google.Transient = false;
        Assert.Equal(1, (await Service().SyncAsync(UserId, default)).Created);
    }

    [Fact]
    public async Task Disconnect_DeletesEvents_RevokesTheToken_AndRemovesTheConnection()
    {
        AddTask("One", Due(1));
        AddTask("Two", Due(2));
        await Service().SyncAsync(UserId, default);

        await Service().DisconnectAsync(UserId, default);

        Assert.Empty(_google.Events);
        Assert.Contains("revoke", _google.Operations);
        Assert.Null(_store.Connection);
        Assert.Empty(_store.Links);
    }

    [Fact]
    public async Task Sync_WithoutConnection_DoesNothing()
    {
        _store.Connection = null;
        AddTask("Open", Due(1));

        var result = await Service().SyncAsync(UserId, default);

        Assert.Equal(0, result.Created);
        Assert.Empty(_google.Operations);
    }
    
    [Fact]
    public async Task Sync_RemovesTheJustCreatedEvent_WhenTheLinkCannotBeSaved()
    {
        AddTask("Open", Due(1));
        _store.FailUpsert = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().SyncAsync(UserId, default));

        Assert.Empty(_google.Events); // дублікат не лишається
        Assert.Empty(_store.Links);
    }

    [Fact]
    public async Task Sync_AdoptsExistingEventsOfTheTask_AndRemovesDuplicates()
    {
        var id = AddTask("Has orphans", Due(1));
        var data = CalendarEventMapper.ToEvent(_store.Tasks[id]);
        _google.Events["orphan-1"] = data;
        _google.Events["orphan-2"] = data; // дублікат, створений попереднім збоєм

        var result = await Service().SyncAsync(UserId, default);

        Assert.Equal(1, result.Created);
        Assert.Single(_google.Events);
        Assert.Equal("orphan-1", _store.Links[id].EventId);
    }
}