using Lighthouse.Api.Data;
using Lighthouse.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lighthouse.Api.Services.Notifications;

public interface IDeviceService
{
    Task RegisterAsync(Guid userId, string fcmToken, CancellationToken ct);
    Task UnregisterAsync(Guid userId, string fcmToken, CancellationToken ct);
}

/// <summary>Реєстрація токенів пристроїв для серверних push-сповіщень (FCM). Не більше 10 пристроїв на користувача.</summary>
public sealed class DeviceService(LighthouseDbContext db) : IDeviceService
{
    public const int MaxPerUser = 10;

    public async Task RegisterAsync(Guid userId, string fcmToken, CancellationToken ct)
    {
        var token = fcmToken.Trim();
        var now = DateTime.UtcNow;

        var existing = await db.DeviceTokens.FirstOrDefaultAsync(d => d.FcmToken == token, ct);
        if (existing is not null)
        {
            existing.UserId = userId; // той самий пристрій, інший обліковий запис
            existing.LastSeenAtUtc = now;
        }
        else
        {
            db.DeviceTokens.Add(new DeviceToken { UserId = userId, FcmToken = token, CreatedAtUtc = now, LastSeenAtUtc = now });
        }

        await db.SaveChangesAsync(ct);

        var stale = await db.DeviceTokens
            .Where(d => d.UserId == userId)
            .OrderByDescending(d => d.LastSeenAtUtc)
            .Skip(MaxPerUser)
            .Select(d => d.Id)
            .ToListAsync(ct);
        if (stale.Count > 0)
        {
            await db.DeviceTokens.Where(d => stale.Contains(d.Id)).ExecuteDeleteAsync(ct);
        }
    }

    public async Task UnregisterAsync(Guid userId, string fcmToken, CancellationToken ct)
    {
        var token = fcmToken.Trim();
        await db.DeviceTokens.Where(d => d.UserId == userId && d.FcmToken == token).ExecuteDeleteAsync(ct);
    }
}