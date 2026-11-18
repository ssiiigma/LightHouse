using Lighthouse.Api.Data;
using Lighthouse.Core.Entities;
using Lighthouse.Shared.Contracts.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Lighthouse.Api.Services.Notifications;

public interface INotificationSettingsService
{
    Task<NotificationSettingsDto> GetAsync(Guid userId, CancellationToken ct);
    Task<NotificationSettingsDto> UpdateAsync(Guid userId, NotificationSettingsDto settings, CancellationToken ct);
}

public sealed class NotificationSettingsService(LighthouseDbContext db) : INotificationSettingsService
{
    public async Task<NotificationSettingsDto> GetAsync(Guid userId, CancellationToken ct)
    {
        var row = await db.NotificationSettings.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId, ct);
        return ToDto(row ?? new NotificationSettings { UserId = userId }); // для давніх облікових записів без рядка
    }

    public async Task<NotificationSettingsDto> UpdateAsync(
        Guid userId, NotificationSettingsDto settings, CancellationToken ct)
    {
        var row = await db.NotificationSettings.FirstOrDefaultAsync(s => s.UserId == userId, ct);
        if (row is null)
        {
            row = new NotificationSettings { UserId = userId };
            db.NotificationSettings.Add(row);
        }

        row.TaskRemindersEnabled = settings.TaskRemindersEnabled;
        row.HabitRemindersEnabled = settings.HabitRemindersEnabled;
        row.DailyDigestEnabled = settings.DailyDigestEnabled;
        row.SmartRemindersEnabled = settings.SmartRemindersEnabled;
        row.QuietHoursStart = settings.QuietHoursStart;
        row.QuietHoursEnd = settings.QuietHoursEnd;

        await db.SaveChangesAsync(ct);
        return ToDto(row);
    }

    private static NotificationSettingsDto ToDto(NotificationSettings s) => new(
        s.TaskRemindersEnabled, s.HabitRemindersEnabled, s.DailyDigestEnabled, s.SmartRemindersEnabled,
        s.QuietHoursStart, s.QuietHoursEnd);
}