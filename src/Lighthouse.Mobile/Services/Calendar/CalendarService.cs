using Lighthouse.Mobile.Services.Api;
using Lighthouse.Shared.Contracts.Calendar;

namespace Lighthouse.Mobile.Services.Calendar;

/// <summary>Підключення Google Calendar: виконується на сервері, тут лише виклики API.</summary>
public interface ICalendar
{
    Task<CalendarStatusDto> GetStatusAsync(CancellationToken ct);

    /// <returns>Адреса входу Google, яку треба відкрити в браузері.</returns>
    Task<string> BeginConnectAsync(CancellationToken ct);

    Task<CalendarSyncResultDto> SyncNowAsync(CancellationToken ct);
    Task DisconnectAsync(CancellationToken ct);
}

public sealed class CalendarService(IApiClient api) : ICalendar
{
    public Task<CalendarStatusDto> GetStatusAsync(CancellationToken ct) =>
        api.GetAsync<CalendarStatusDto>("api/calendar/status", ct);

    public async Task<string> BeginConnectAsync(CancellationToken ct) =>
        (await api.PostAsync<object, CalendarConnectDto>("api/calendar/connect", new { }, ct)).AuthorizationUrl;

    public Task<CalendarSyncResultDto> SyncNowAsync(CancellationToken ct) =>
        api.PostAsync<object, CalendarSyncResultDto>("api/calendar/sync", new { }, ct);

    public Task DisconnectAsync(CancellationToken ct) => api.DeleteAsync("api/calendar", ct);
}