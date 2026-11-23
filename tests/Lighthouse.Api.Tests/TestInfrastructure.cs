using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Lighthouse.Api.Options;
using Lighthouse.Shared.Contracts.Auth;
using Lighthouse.Shared.Contracts.Categories;
using Lighthouse.Shared.Contracts.Habits;
using Lighthouse.Shared.Contracts.Tasks;
using Lighthouse.Shared.Enums;
using Lighthouse.Api.Services.Email;
using Lighthouse.Api.Services.Auth;
using Lighthouse.Api.Services.Calendar;
using Lighthouse.Api.Services.Push;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;

namespace Lighthouse.Api.Tests;

/// <summary>
/// Інтеграційні тести працюють із реальним SQL Server (той самий контейнер, що й для розробки), але в окремій
/// базі LighthouseTests_*, яка видаляється після завершення тестів. Рядок підключення до сервера (без бази)
/// береться зі змінної середовища LIGHTHOUSE_TEST_SQL; без неї тести пропускаються.
/// </summary>
internal static class TestEnvironment
{
    public const string SqlVariable = "LIGHTHOUSE_TEST_SQL";

    private static readonly string? ServerConnection = Environment.GetEnvironmentVariable(SqlVariable);
    private static readonly string DatabaseName = $"LighthouseTests_{Guid.NewGuid():N}";
    private static readonly object Gate = new();
    private static bool _initialized;

    public static bool IsConfigured => !string.IsNullOrWhiteSpace(ServerConnection);

    public static void Initialize()
    {
        lock (Gate)
        {
            if (_initialized || !IsConfigured)
            {
                return;
            }

            VerifyServerConnection(); // швидка помилка замість багатохвилинних повторів у кожному тесті
            _initialized = true;

            var connection = new SqlConnectionStringBuilder(ServerConnection!) { InitialCatalog = DatabaseName };

            // Змінні середовища мають вищий пріоритет за appsettings і user-secrets.
            Environment.SetEnvironmentVariable("ConnectionStrings__Default", connection.ConnectionString);
            Environment.SetEnvironmentVariable("Recommendations__ModelTrainingEnabled", "false");
            Environment.SetEnvironmentVariable("Workers__BackupEnabled", "false");
            Environment.SetEnvironmentVariable("Workers__MaintenanceEnabled", "false");
            Environment.SetEnvironmentVariable("Workers__CalendarEnabled", "false");
            Environment.SetEnvironmentVariable("Workers__PushEnabled", "false");
            AppDomain.CurrentDomain.ProcessExit += (_, _) => DropDatabase();
        }
    }

    private static void VerifyServerConnection()
    {
        var master = new SqlConnectionStringBuilder(ServerConnection!)
        {
            InitialCatalog = "master",
            ConnectTimeout = 5,
            ConnectRetryCount = 0
        };

        try
        {
            using var connection = new SqlConnection(master.ConnectionString);
            connection.Open();
        }
        catch (SqlException ex)
        {
            throw new InvalidOperationException(
                $"Не вдалося підключитися до SQL Server зі змінної {SqlVariable}: {ex.Message} " +
                "Перевірте пароль і що контейнер lighthouse-db запущений (docker compose ps).", ex);
        }
    }

    private static void DropDatabase()
    {
        try
        {
            SqlConnection.ClearAllPools();
            var master = new SqlConnectionStringBuilder(ServerConnection!) { InitialCatalog = "master" };
            using var connection = new SqlConnection(master.ConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                $"IF DB_ID(N'{DatabaseName}') IS NOT NULL BEGIN " +
                $"ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                $"DROP DATABASE [{DatabaseName}]; END";
            command.ExecuteNonQuery();
        }
        catch
        {
            // Прибирання «за можливістю»: тестова база не впливає на дані розробки.
        }
    }
}

public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (!TestEnvironment.IsConfigured)
        {
            Skip = $"Задайте змінну середовища {TestEnvironment.SqlVariable} (рядок підключення до SQL Server без бази).";
        }
    }
}

/// <summary>Збирає події Serilog у пам'яті (для перевірки, що в журналах немає секретів).</summary>
public sealed class CapturingSink : ILogEventSink
{
    private readonly ConcurrentQueue<string> _lines = new();

    public void Emit(LogEvent logEvent)
    {
        var properties = string.Join(' ', logEvent.Properties.Select(p => $"{p.Key}={p.Value}"));
        _lines.Enqueue($"{logEvent.RenderMessage()} {properties} {logEvent.Exception}");
    }

    public IReadOnlyList<string> Snapshot() => _lines.ToArray();
}

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly Action<RateLimitOptions>? _configureLimits;
    public TestApi.CapturingEmailQueue Emails { get; } = new();

    public CapturingSink Logs { get; } = new();
    
    public FakePushSender Push { get; } = new();
    
    public FakeGoogleCalendarClient Calendar { get; } = new();

    public ApiFactory(Action<RateLimitOptions>? configureLimits = null)
    {
        TestEnvironment.Initialize();
        _configureLimits = configureLimits;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            // За замовчуванням ліміти настільки великі, що не заважають іншим тестам.
            services.PostConfigure<RateLimitOptions>(options =>
            {
                options.AuthPermitLimit = 10_000;
                options.AuthWindowSeconds = 60;
                options.ApiPermitLimit = 100_000;
                options.ApiWindowSeconds = 60;
                _configureLimits?.Invoke(options);
            });
            services.AddSingleton<IEmailQueue>(Emails);
            services.AddSingleton<ILogEventSink>(Logs);
            services.AddSingleton<IGoogleTokenValidator, FakeGoogleTokenValidator>();
            services.AddSingleton<IGoogleCalendarClient>(Calendar);
            services.AddSingleton<IPushSender>(Push);
        });
    }
}

public sealed class ApiFixture : IDisposable
{
    public ApiFactory Factory { get; } = new();

    public void Dispose() => Factory.Dispose();
}

/// <summary>Усі тести виконуються послідовно, бо працюють з однією тестовою базою.</summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "Api";
}

public sealed record Session(HttpClient Client, AuthResponse Auth, string Email, string Password);

public static class TestApi
{
    public const string DefaultPassword = "Passw0rd!Test";

    public static async Task<Session> RegisterAsync(this ApiFactory factory, string? password = null)
    {
        var email = $"user-{Guid.NewGuid():N}@example.com";
        password ??= DefaultPassword;

        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Test User", email, password));
        response.EnsureSuccessStatusCode();

        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return new Session(client, auth, email, password);
    }

    public static UpsertTaskRequest NewTask(string title) =>
        new(title, null, null, TaskPriority.Medium, null, null, null, null, [], DateTime.UtcNow);

    public static UpsertCategoryRequest NewCategory(string name) =>
        new(name, "#4C8BF5", null, DateTime.UtcNow);

    public static UpsertHabitRequest NewHabit(string title) =>
        new(title, null, HabitFrequencyType.Daily, 0, null, null, null, null, null, DateTime.UtcNow);
    
    /// <summary>Замість SMTP зберігає листи в пам'яті, щоб тести могли прочитати токени.</summary>
    public sealed class CapturingEmailQueue : IEmailQueue
    {
        private readonly ConcurrentQueue<EmailMessage> _messages = new();

        public bool TryEnqueue(EmailMessage message)
        {
            _messages.Enqueue(message);
            return true;
        }

        public IReadOnlyList<EmailMessage> To(string address) =>
            _messages.Where(m => string.Equals(m.To, address, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}

/// <summary>Замість запиту до Google розбирає токен виду «subject|email|verified|name».</summary>
public sealed class FakeGoogleTokenValidator : IGoogleTokenValidator
{
    public static string Token(string subject, string email, bool verified = true, string name = "Google User") =>
        $"{subject}|{email}|{verified}|{name}";

    public Task<GoogleIdentity?> ValidateAsync(string idToken, CancellationToken ct)
    {
        var parts = idToken.Split('|');
        return Task.FromResult<GoogleIdentity?>(
            parts.Length == 4 && bool.TryParse(parts[2], out var verified)
                ? new GoogleIdentity(parts[0], parts[1], verified, parts[3])
                : null);
    }
}

/// <summary>Замість Google: пам'ятає створені події й журнал викликів, може імітувати відкликання доступу й збої.</summary>
public sealed class FakeGoogleCalendarClient : IGoogleCalendarClient
{
    private int _next;

    public ConcurrentDictionary<string, CalendarEventData> Events { get; } = new();
    public ConcurrentQueue<string> Operations { get; } = new();
    public bool InvalidGrant { get; set; }
    public bool Transient { get; set; }

    public string BuildAuthorizationUrl(string state, string redirectUri) =>
        $"https://accounts.example.test/auth?state={Uri.EscapeDataString(state)}&redirect_uri={Uri.EscapeDataString(redirectUri)}";

    public Task<GoogleTokenResponse> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct) =>
        code == "bad"
            ? throw new CalendarAuthException("bad code")
            : Task.FromResult(new GoogleTokenResponse("refresh-token", "access-token", 3600));

    public Task<string> GetAccessTokenAsync(Guid userId, string refreshToken, CancellationToken ct) =>
        InvalidGrant ? throw new CalendarAuthException("revoked") : Task.FromResult("access-token");

    public Task<string> CreateEventAsync(string accessToken, string calendarId, CalendarEventData data, CancellationToken ct)
    {
        ThrowIfTransient();

        var existing = Events.Where(e => e.Value.TaskId == data.TaskId).Select(e => e.Key).OrderBy(k => k).ToList();
        if (existing.Count > 0)
        {
            foreach (var extra in existing.Skip(1))
            {
                Events.TryRemove(extra, out _);
                Operations.Enqueue("delete:" + extra);
            }

            Events[existing[0]] = data;
            Operations.Enqueue("adopt:" + existing[0]);
            return Task.FromResult(existing[0]);
        }

        var id = "evt-" + Interlocked.Increment(ref _next);
        Events[id] = data;
        Operations.Enqueue("create:" + id);
        return Task.FromResult(id);
    }

    public Task<bool> UpdateEventAsync(string accessToken, string calendarId, string eventId, CalendarEventData data, CancellationToken ct)
    {
        ThrowIfTransient();
        if (!Events.ContainsKey(eventId))
        {
            return Task.FromResult(false);
        }

        Events[eventId] = data;
        Operations.Enqueue("update:" + eventId);
        return Task.FromResult(true);
    }

    public Task DeleteEventAsync(string accessToken, string calendarId, string eventId, CancellationToken ct)
    {
        ThrowIfTransient();
        Events.TryRemove(eventId, out _);
        Operations.Enqueue("delete:" + eventId);
        return Task.CompletedTask;
    }

    public Task RevokeAsync(string token, CancellationToken ct)
    {
        Operations.Enqueue("revoke");
        return Task.CompletedTask;
    }

    private void ThrowIfTransient()
    {
        if (Transient)
        {
            throw new CalendarTransientException("simulated");
        }
    }
}

/// <summary>Замість FCM: запам'ятовує надіслане; окремі токени можна оголосити недійсними.</summary>
public sealed class FakePushSender : IPushSender
{
    public ConcurrentQueue<(string Token, IReadOnlyDictionary<string, string> Data)> Sent { get; } = new();
    public ConcurrentDictionary<string, bool> UnregisteredTokens { get; } = new();

    public bool IsConfigured => true;

    public Task<PushResult> SendAsync(string deviceToken, IReadOnlyDictionary<string, string> data, CancellationToken ct)
    {
        if (UnregisteredTokens.ContainsKey(deviceToken))
        {
            return Task.FromResult(PushResult.Unregistered);
        }

        Sent.Enqueue((deviceToken, data));
        return Task.FromResult(PushResult.Sent);
    }
}