using Lighthouse.Shared.Contracts.Calendar;

namespace Lighthouse.Api.Services.Calendar;

public sealed record CalendarConnectionInfo(Guid UserId, string RefreshToken, string CalendarId);

public sealed record CalendarLinkInfo(Guid TaskId, string EventId, DateTime SyncedAtUtc);

public sealed record CalendarTaskInfo(
    Guid Id, string Title, string? Description, DateTime? DeadlineUtc, DateTime? ReminderAtUtc, int? EstimatedMinutes,
    bool IsCompleted, bool IsDeleted, DateTime ServerModifiedAtUtc);

public sealed record CalendarEventData(
    Guid TaskId, string Summary, string? Description, DateTime StartUtc, DateTime EndUtc, int? ReminderMinutes);

public sealed record GoogleTokenResponse(string RefreshToken, string AccessToken, int ExpiresInSeconds);

/// <summary>Доступ відкликано або токен недійсний: календар треба підключити заново.</summary>
public sealed class CalendarAuthException(string message) : Exception(message);

/// <summary>Тимчасова помилка (мережа, ліміти, збій Google): спробуємо в наступному циклі.</summary>
public sealed class CalendarTransientException(string message) : Exception(message);

/// <summary>Google відхилив саме цей запит (некоректні дані): повторення нічого не змінить.</summary>
public sealed class CalendarRejectedException(string message) : Exception(message);

/// <summary>Сховище підключення й зв'язків «завдання → подія» (реалізація знає про сутності БД).</summary>
public interface ICalendarStore
{
    Task<bool> IsConnectedAsync(Guid userId, CancellationToken ct);
    Task<CalendarConnectionInfo?> GetConnectionAsync(Guid userId, CancellationToken ct);
    Task SaveConnectionAsync(Guid userId, string refreshToken, string calendarId, CancellationToken ct);
    Task<IReadOnlyList<Guid>> ConnectedUserIdsAsync(CancellationToken ct);

    /// <summary>Завдання, що мають або можуть мати подію: відкриті з дедлайном і ті, для яких уже є зв'язок.</summary>
    Task<IReadOnlyList<CalendarTaskInfo>> GetTasksAsync(Guid userId, CancellationToken ct);

    Task<IReadOnlyList<CalendarLinkInfo>> GetLinksAsync(Guid userId, CancellationToken ct);
    Task UpsertLinkAsync(Guid taskId, string eventId, DateTime syncedAtUtc, CancellationToken ct);
    Task RemoveLinkAsync(Guid taskId, CancellationToken ct);

    /// <summary>Видаляє підключення разом із зв'язками подій користувача.</summary>
    Task RemoveConnectionAsync(Guid userId, CancellationToken ct);
}

public interface IGoogleCalendarClient
{
    string BuildAuthorizationUrl(string state, string redirectUri);
    Task<GoogleTokenResponse> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct);

    /// <exception cref="CalendarAuthException">Токен відкликано.</exception>
    Task<string> GetAccessTokenAsync(Guid userId, string refreshToken, CancellationToken ct);

    Task<string> CreateEventAsync(string accessToken, string calendarId, CalendarEventData data, CancellationToken ct);

    /// <returns>false, якщо події більше немає (видалена в календарі).</returns>
    Task<bool> UpdateEventAsync(string accessToken, string calendarId, string eventId, CalendarEventData data, CancellationToken ct);

    Task DeleteEventAsync(string accessToken, string calendarId, string eventId, CancellationToken ct);
    Task RevokeAsync(string token, CancellationToken ct);
}

public interface ICalendarSyncService
{
    Task<CalendarSyncResultDto> SyncAsync(Guid userId, CancellationToken ct);

    /// <summary>Видаляє створені події (за можливості), відкликає токен і прибирає підключення.</summary>
    Task DisconnectAsync(Guid userId, CancellationToken ct);
}