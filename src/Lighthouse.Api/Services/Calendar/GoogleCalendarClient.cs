using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lighthouse.Api.Options;
using Lighthouse.Shared.Auth;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Lighthouse.Api.Services.Calendar;

/// <summary>Клієнт Google OAuth і Calendar API (REST). Тіла відповідей і токени у журнал не потрапляють.</summary>
public sealed class GoogleCalendarClient(
    IHttpClientFactory factory, IMemoryCache cache, IOptions<GoogleOptions> options, ILogger<GoogleCalendarClient> logger)
    : IGoogleCalendarClient
{
    public const string Scope = "openid email https://www.googleapis.com/auth/calendar.events";
    private const string TaskIdProperty = "lighthouseTaskId";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string RevokeEndpoint = "https://oauth2.googleapis.com/revoke";
    private const string CalendarsBase = "https://www.googleapis.com/calendar/v3/calendars/";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private HttpClient Http => factory.CreateClient("google");
    private GoogleOptions Settings => options.Value;

    public string BuildAuthorizationUrl(string state, string redirectUri)
    {
        var parameters = new (string Name, string Value)[]
        {
            ("client_id", Settings.WebClientId),
            ("redirect_uri", redirectUri),
            ("response_type", "code"),
            ("scope", Scope),
            ("access_type", "offline"),
            ("prompt", "consent"), // щоб Google щоразу видавав refresh-токен
            ("state", state)
        };

        return GoogleOAuth.AuthorizationEndpoint + "?" +
               string.Join("&", parameters.Select(p => $"{p.Name}={Uri.EscapeDataString(p.Value)}"));
    }

    public async Task<GoogleTokenResponse> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct)
    {
        using var response = await PostFormAsync(TokenEndpoint, new Dictionary<string, string>
        {
            ["client_id"] = Settings.WebClientId,
            ["client_secret"] = Settings.WebClientSecret,
            ["code"] = code,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = redirectUri
        }, ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new CalendarAuthException($"The authorization code was rejected ({(int)response.StatusCode}).");
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = document.RootElement;
        var refresh = root.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
        var access = root.TryGetProperty("access_token", out var a) ? a.GetString() : null;
        if (string.IsNullOrEmpty(refresh) || string.IsNullOrEmpty(access))
        {
            throw new CalendarAuthException("Google did not return a refresh token.");
        }

        return new GoogleTokenResponse(refresh, access, root.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600);
    }

    public async Task<string> GetAccessTokenAsync(Guid userId, string refreshToken, CancellationToken ct)
    {
        var key = $"calendar-access:{userId}";
        if (cache.TryGetValue(key, out string? cached) && cached is not null)
        {
            return cached;
        }

        using var response = await PostFormAsync(TokenEndpoint, new Dictionary<string, string>
        {
            ["client_id"] = Settings.WebClientId,
            ["client_secret"] = Settings.WebClientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token"
        }, ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized && body.Contains("invalid_grant"))
        {
            throw new CalendarAuthException("The refresh token is no longer valid.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new CalendarTransientException($"Token refresh failed ({(int)response.StatusCode}).");
        }

        using var document = JsonDocument.Parse(body);
        var token = document.RootElement.GetProperty("access_token").GetString()!;
        var expires = document.RootElement.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600;
        cache.Set(key, token, TimeSpan.FromSeconds(Math.Max(60, expires - 120)));
        return token;
    }

        public async Task<string> CreateEventAsync(
        string accessToken, string calendarId, CalendarEventData data, CancellationToken ct)
    {
        // Ідемпотентність: якщо подія цього завдання вже є (збій після створення, дублікати), використовуємо її.
        var existing = await FindByTaskAsync(accessToken, calendarId, data.TaskId, ct);
        if (existing.Count > 0)
        {
            var keep = existing[0];
            foreach (var extra in existing.Skip(1))
            {
                await DeleteEventAsync(accessToken, calendarId, extra, ct);
            }

            if (await UpdateEventAsync(accessToken, calendarId, keep, data, ct))
            {
                return keep;
            }
        }

        using var response = await SendAsync(HttpMethod.Post, EventsUrl(calendarId), accessToken, Body(data), ct);
        EnsureSuccess(response);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return document.RootElement.GetProperty("id").GetString()!;
    }

    private async Task<List<string>> FindByTaskAsync(string accessToken, string calendarId, Guid taskId, CancellationToken ct)
    {
        var query = "?privateExtendedProperty=" + Uri.EscapeDataString($"{TaskIdProperty}={taskId:N}") +
                    "&showDeleted=false&maxResults=50&fields=items(id)";
        using var response = await SendAsync(HttpMethod.Get, EventsUrl(calendarId) + query, accessToken, null, ct);
        EnsureSuccess(response);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return document.RootElement.TryGetProperty("items", out var items)
            ? items.EnumerateArray().Select(item => item.GetProperty("id").GetString()!).ToList()
            : [];
    }

    private static object Body(CalendarEventData data) => new
    {
        summary = data.Summary,
        description = data.Description,
        start = new { dateTime = data.StartUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", Inv), timeZone = "UTC" },
        end = new { dateTime = data.EndUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", Inv), timeZone = "UTC" },
        extendedProperties = new
        {
            @private = new Dictionary<string, string> { [TaskIdProperty] = data.TaskId.ToString("N") }
        },
        reminders = data.ReminderMinutes is { } minutes
            ? (object)new { useDefault = false, overrides = new[] { new { method = "popup", minutes } } }
            : new { useDefault = true }
    };

    public async Task<bool> UpdateEventAsync(
        string accessToken, string calendarId, string eventId, CalendarEventData data, CancellationToken ct)
    {
        using var response = await SendAsync(
            HttpMethod.Patch, EventsUrl(calendarId) + "/" + Uri.EscapeDataString(eventId), accessToken, Body(data), ct);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            return false;
        }

        EnsureSuccess(response);
        return true;
    }

    public async Task DeleteEventAsync(string accessToken, string calendarId, string eventId, CancellationToken ct)
    {
        using var response = await SendAsync(
            HttpMethod.Delete, EventsUrl(calendarId) + "/" + Uri.EscapeDataString(eventId), accessToken, null, ct);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            return; // події вже немає
        }

        EnsureSuccess(response);
    }

    public async Task RevokeAsync(string token, CancellationToken ct)
    {
        try
        {
            using var response = await PostFormAsync(RevokeEndpoint, new Dictionary<string, string> { ["token"] = token }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogInformation("The Google token could not be revoked ({Type}).", ex.GetType().Name);
        }
    }

    private static string EventsUrl(string calendarId) => CalendarsBase + Uri.EscapeDataString(calendarId) + "/events";

    private async Task<HttpResponseMessage> PostFormAsync(string url, Dictionary<string, string> form, CancellationToken ct)
    {
        try
        {
            return await Http.PostAsync(url, new FormUrlEncodedContent(form), ct);
        }
        catch (HttpRequestException ex)
        {
            throw new CalendarTransientException("Google is unreachable: " + ex.GetType().Name);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new CalendarTransientException("Google did not respond in time.");
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string url, string accessToken, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        try
        {
            return await Http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new CalendarTransientException("Google is unreachable: " + ex.GetType().Name);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new CalendarTransientException("Google did not respond in time.");
        }
    }

    /// <summary>2xx — успіх; 401, 403, 429 і 5xx — тимчасові; решта 4xx — Google відхилив запит.</summary>
    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var code = (int)response.StatusCode;
        if (code is 401 or 403 or 429 or >= 500)
        {
            throw new CalendarTransientException($"Google Calendar answered {code}.");
        }

        throw new CalendarRejectedException($"Google Calendar rejected the request ({code}).");
    }
}