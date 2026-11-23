using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lighthouse.Mobile.Data;
using Lighthouse.Mobile.Infrastructure;
using Lighthouse.Mobile.Localization;
using Lighthouse.Mobile.Services.Habits;
using Lighthouse.Mobile.Services.Sync;
using Lighthouse.Mobile.Services.Tasks;
using Lighthouse.Shared.Enums;
using Lighthouse.Shared.Notifications;
using Microsoft.Extensions.Logging;

namespace Lighthouse.Mobile.Services.Notifications;

public sealed record NotifStatus(bool Enabled, bool Exact, bool BatteryUnrestricted);

/// <summary>Керування локальними нагадуваннями: що, коли й з яким текстом планувати.</summary>
public interface INotif
{
    /// <summary>Підписується на зміни даних і налаштувань; запускає планування.</summary>
    void Start();

    /// <summary>Планування з невеликою затримкою (зливає серію змін в одне).</summary>
    void RequestReschedule();

    Task RescheduleAsync();
    Task CancelAllAsync();
    Task<NotifStatus> GetStatusAsync();
    Task<bool> RequestPermissionAsync();
}

public sealed class NotifManager(
    ILocalDB localDb, ILocalNotif local, INotifSettings settings, ISystemSettings system,
    ISyncSvc sync, ITaskSvc tasks, IHabitSvc habits, LocalizationService loc, ILogger<NotifManager> logger) : INotif
{
    private const string StoredKey = "notif.scheduled";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _pending;
    private bool _started;

    public void Start()
    {
        if (_started)
        {
            RequestReschedule();
            return;
        }

        _started = true;
        tasks.Changed += RequestReschedule;
        habits.Changed += RequestReschedule;
        sync.DataChanged += RequestReschedule;
        settings.Changed += RequestReschedule;
        loc.PropertyChanged += (_, _) => RequestReschedule();
        RequestReschedule();
    }

    public void RequestReschedule()
    {
        var cts = new CancellationTokenSource();
        Interlocked.Exchange(ref _pending, cts)?.Cancel();

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(700, cts.Token);
                await RescheduleAsync();
            }
            catch (OperationCanceledException)
            {
                // замінено новішим запитом
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Reminder scheduling failed.");
            }
        });
    }

    public async Task RescheduleAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (Preferences.Default.Get(PreferenceKeys.UserId, string.Empty).Length == 0)
            {
                await CancelAllCoreAsync(); // немає облікового запису
                return;
            }

            if (!await local.AreEnabledAsync())
            {
                return;
            }

            var db = await localDb.GetConnectionAsync();
            var taskRows = await db.Table<LocalTask>().Where(t => !t.IsDeleted).ToListAsync();
            var habitRows = await db.Table<LocalHabit>().Where(h => !h.IsDeleted).ToListAsync();
            var logRows = await db.Table<LocalHabitLog>().ToListAsync();

            var logs = logRows.GroupBy(l => l.HabitId)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<DateOnly>)g.Select(l => HabitKeys.Parse(l.Date)).ToList());

            var plan = NotificationPlanner.Plan(
                DateTime.Now,
                settings.Load(),
                taskRows.Select(t => new PlanTask(
                    t.Id, t.ReminderAtUtc is { } r ? ToLocal(r) : null, t.CompletedAtUtc is not null)),
                habitRows.Select(h => new PlanHabit(
                    h.Id, h.FrequencyType, h.DaysOfWeekMask, h.TimesPerWeek, h.ReminderMinutes,
                    logs.GetValueOrDefault(h.Id) ?? [])));

            var taskById = taskRows.ToDictionary(t => t.Id);
            var habitById = habitRows.ToDictionary(h => h.Id);
            var stored = LoadStored();
            var desired = new Dictionary<int, string>();

            foreach (var item in plan)
            {
                var (title, body) = item.Kind == NotificationKind.TaskReminder
                    ? TaskText(taskById[item.TargetId])
                    : (habitById[item.TargetId].Title, loc["Notif_HabitBody"]);

                var signature = $"{item.AtLocal.Ticks}|{Hash(title + "|" + body)}|ch2";
                desired[item.Id] = signature;

                if (stored.TryGetValue(item.Id, out var old) && old == signature)
                {
                    continue; // без змін
                }

                try
                {
                    await local.CancelAsync(item.Id);
                    await local.ScheduleAsync(item.Id, title, body, item.AtLocal);
                }
                catch (Exception ex)
                {
                    desired.Remove(item.Id); // спробуємо знову при наступному плануванні
                    logger.LogWarning(ex, "Could not schedule notification {Id}.", item.Id);
                }
            }

            foreach (var staleId in stored.Keys.Where(id => !desired.ContainsKey(id)))
            {
                await local.CancelAsync(staleId);
            }

            SaveStored(desired);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CancelAllAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await CancelAllCoreAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<NotifStatus> GetStatusAsync() =>
        new(await local.AreEnabledAsync(), system.CanScheduleExactAlarms, system.IsIgnoringBatteryOptimizations);

    public async Task<bool> RequestPermissionAsync()
    {
        var granted = await local.RequestPermissionAsync();
        RequestReschedule();
        return granted;
    }

    private async Task CancelAllCoreAsync()
    {
        await local.CancelAllAsync();
        Preferences.Default.Remove(StoredKey);
    }

    private (string Title, string Body) TaskText(LocalTask task) =>
        (task.Title, task.DeadlineUtc is { } deadline
            ? loc.Get("Notif_TaskDeadline", ToLocal(deadline).ToString("ddd d MMM, t", CultureInfo.CurrentCulture))
            : loc["Notif_TaskBody"]);

    private static Dictionary<int, string> LoadStored()
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<int, string>>(Preferences.Default.Get(StoredKey, "{}")) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    private static void SaveStored(Dictionary<int, string> map) =>
        Preferences.Default.Set(StoredKey, JsonSerializer.Serialize(map));

    private static string Hash(string text) =>
        Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(text)))[..8];

    private static DateTime ToLocal(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
}