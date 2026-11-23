using System.Globalization;
using Lighthouse.Api.Data;
using Lighthouse.Api.Infrastructure;
using Lighthouse.Api.Options;
using Lighthouse.Api.Services.Reports;
using Lighthouse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Lighthouse.Api.Services.Push;

/// <summary>Раз на хвилину обходить користувачів із зареєстрованими пристроями й надсилає те, що визначив <see cref="PushPlanner"/>.</summary>
public sealed class PushScheduler(
    LighthouseDbContext db, IPushSender sender, IPushLog log, IUserTimeService userTime,
    IOptions<PushOptions> options, ILogger<PushScheduler> logger)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public async Task<int> RunAsync(DateTime nowUtc, CancellationToken ct)
    {
        if (!sender.IsConfigured)
        {
            return 0;
        }

        var userIds = await db.DeviceTokens.AsNoTracking().Select(d => d.UserId).Distinct().ToListAsync(ct);
        var sent = 0;

        foreach (var userId in userIds)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                sent += await ProcessAsync(userId, nowUtc, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Push processing failed for user {UserId}.", userId);
                db.ChangeTracker.Clear();
            }
        }

        return sent;
    }

    private async Task<int> ProcessAsync(Guid userId, DateTime nowUtc, CancellationToken ct)
    {
        var settings = await db.NotificationSettings.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId, ct)
                       ?? new NotificationSettings { UserId = userId };
        if (!settings.DailyDigestEnabled && !settings.SmartRemindersEnabled)
        {
            return 0;
        }

        var zone = await userTime.GetZoneAsync(userId, ct);
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone);
        var dayStartUtc = LocalToUtc(nowLocal.Date, zone);
        var dayEndUtc = dayStartUtc.AddHours(24);

        var open = await db.Tasks.AsNoTracking()
            .Where(t => t.UserId == userId && !t.IsDeleted && t.CompletedAtUtc == null)
            .Select(t => new PushTask(t.Id, t.Title, t.Priority, t.DeadlineUtc))
            .Take(2000)
            .ToListAsync(ct);
        var since = nowUtc.AddDays(-90);
        var completed = await db.Tasks.AsNoTracking()
            .Where(t => t.UserId == userId && !t.IsDeleted && t.CompletedAtUtc != null && t.CompletedAtUtc >= since)
            .Select(t => t.CompletedAtUtc!.Value)
            .Take(5000)
            .ToListAsync(ct);

        var plan = PushPlanner.Plan(new PushUserSnapshot(
            nowUtc, zone, TimeSpan.FromHours(Math.Clamp(options.Value.DigestHour, 0, 23)),
            settings.DailyDigestEnabled, settings.SmartRemindersEnabled,
            settings.QuietHoursStart, settings.QuietHoursEnd,
            open, completed,
            await log.SentBetweenAsync(userId, PushKind.Digest, dayStartUtc, dayEndUtc, ct),
            await log.SentBetweenAsync(userId, PushKind.Smart, dayStartUtc, dayEndUtc, ct)));
        if (plan.Count == 0)
        {
            return 0;
        }

        var devices = await db.DeviceTokens.Where(d => d.UserId == userId).ToListAsync(ct);
        var sent = 0;
        foreach (var push in plan)
        {
            var data = ToData(push);
            var delivered = false;

            foreach (var device in devices.ToList())
            {
                switch (await sender.SendAsync(device.FcmToken, data, ct))
                {
                    case PushResult.Sent:
                        delivered = true;
                        break;
                    case PushResult.Unregistered:
                        db.DeviceTokens.Remove(device);
                        devices.Remove(device);
                        await db.SaveChangesAsync(ct);
                        break;
                }
            }

            if (delivered)
            {
                await log.RecordAsync(userId, push.Kind, push.Task.Id, nowUtc, ct);
                sent++;
            }
        }

        return sent;
    }

    private static Dictionary<string, string> ToData(PlannedPush push) => new()
    {
        ["kind"] = push.Kind == PushKind.Digest ? "digest" : "smart",
        ["count"] = push.Count.ToString(Inv),
        ["task"] = push.Task.Title.Length <= 80 ? push.Task.Title : push.Task.Title[..80],
        ["taskId"] = push.Task.Id.ToString("N")
    };

    private static DateTime LocalToUtc(DateTime localDate, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(localDate, DateTimeKind.Unspecified);
        try
        {
            return TimeZoneInfo.ConvertTimeToUtc(unspecified, zone);
        }
        catch (ArgumentException)
        {
            return DateTime.SpecifyKind(unspecified, DateTimeKind.Utc); // неіснуючий час через перехід на літній
        }
    }
}