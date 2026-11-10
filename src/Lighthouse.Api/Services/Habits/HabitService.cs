using System.Globalization;
using System.Text.Json;
using Lighthouse.Api.Data;
using Lighthouse.Api.Infrastructure;
using Lighthouse.Api.Services.Behavior;
using Lighthouse.Core.Entities;
using Lighthouse.Core.Habits;
using Lighthouse.Shared.Contracts.Habits;
using Lighthouse.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Lighthouse.Api.Services.Habits;

public sealed class HabitService(
    LighthouseDbContext db,
    IBehaviorRecorder behavior,
    IUserTimeService userTime) : IHabitService
{
    public async Task<IReadOnlyList<HabitDto>> GetAsync(Guid userId, DateTime? modifiedSinceUtc, CancellationToken ct)
    {
        var query = db.Habits.AsNoTracking().Include(h => h.Logs).Where(h => h.UserId == userId);

        query = SyncTime.ToUtc(modifiedSinceUtc) is { } since
            ? query.Where(h => h.ServerModifiedAtUtc > since)
            : query.Where(h => !h.IsDeleted);

        var items = await query.OrderBy(h => h.ServerModifiedAtUtc).ToListAsync(ct);
        var today = await TodayAsync(userId, ct);
        return items.Select(h => ToDto(h, today)).ToList();
    }

    public async Task<HabitDto> UpsertAsync(Guid userId, Guid id, UpsertHabitRequest request, CancellationToken ct)
    {
        if (request.CategoryId is { } categoryId &&
            !await db.Categories.AnyAsync(c => c.Id == categoryId && c.UserId == userId && !c.IsDeleted, ct))
        {
            throw new AppException(StatusCodes.Status400BadRequest, "Category not found.");
        }

        var habit = await db.Habits.Include(h => h.Logs).FirstOrDefaultAsync(h => h.Id == id, ct);
        if (habit is not null && habit.UserId != userId)
        {
            throw new AppException(StatusCodes.Status404NotFound, "Habit not found.");
        }

        var now = DateTime.UtcNow;
        var editedAt = SyncTime.ClampToNow(request.ClientUpdatedAtUtc, now);
        var today = await TodayAsync(userId, ct);

        if (habit is null)
        {
            habit = new Habit { Id = id, UserId = userId, CreatedAtUtc = now };
            db.Habits.Add(habit);
        }
        else if (habit.UpdatedAtUtc > editedAt)
        {
            return ToDto(habit, today); // «останній запис»
        }

        habit.Title = request.Title.Trim();
        habit.Description = request.Description;
        habit.FrequencyType = request.FrequencyType;
        // Зберігаємо лише параметри, релевантні обраному режиму частоти.
        habit.DaysOfWeekMask = request.FrequencyType == HabitFrequencyType.SpecificDays ? request.DaysOfWeekMask : 0;
        habit.TimesPerWeek = request.FrequencyType == HabitFrequencyType.TimesPerWeek ? request.TimesPerWeek : null;
        habit.ReminderTime = request.ReminderTime;
        habit.ColorHex = request.ColorHex;
        habit.Icon = request.Icon;
        habit.CategoryId = request.CategoryId;
        habit.IsDeleted = false;
        habit.UpdatedAtUtc = editedAt;
        habit.ServerModifiedAtUtc = now;

        await db.SaveChangesAsync(ct);
        return ToDto(habit, today);
    }

    public async Task DeleteAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var habit = await db.Habits.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (habit is null)
        {
            return; // ідемпотентно
        }

        if (habit.UserId != userId)
        {
            throw new AppException(StatusCodes.Status404NotFound, "Habit not found.");
        }

        if (habit.IsDeleted)
        {
            return;
        }

        var now = DateTime.UtcNow;
        habit.IsDeleted = true; // архів; фізичне видалення (разом із відмітками) — фонова задача через 14 днів
        habit.UpdatedAtUtc = now;
        habit.ServerModifiedAtUtc = now;

        await db.SaveChangesAsync(ct);
    }

    public async Task<HabitDto> PutLogAsync(
        Guid userId, Guid habitId, DateOnly date, DateTime? completedAtUtc, CancellationToken ct)
    {
        var habit = await FindActiveAsync(userId, habitId, ct);
        var now = DateTime.UtcNow;
        var today = await TodayAsync(userId, ct);

        if (date > today.AddDays(1)) // запас на різницю часових поясів
        {
            throw new AppException(StatusCodes.Status400BadRequest, "Date cannot be in the future.");
        }

        if (habit.Logs.All(l => l.Date != date))
        {
            var completedAt = completedAtUtc is { } c ? SyncTime.ClampToNow(c, now) : now;

            var log = new HabitLog
            {
                HabitId = habit.Id,
                Date = date,
                CompletedAtUtc = completedAt,
                UpdatedAtUtc = now,
                ServerModifiedAtUtc = now
            };
            db.HabitLogs.Add(log);
            if (!habit.Logs.Contains(log))
            {
                habit.Logs.Add(log);
            }

            // Лише курсор синхронізації: UpdatedAtUtc звички (час редагування полів) не змінюється.
            habit.ServerModifiedAtUtc = now;

            var payload = JsonSerializer.Serialize(new
            {
                date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            });
            await behavior.RecordAsync(userId, BehaviorEventType.HabitChecked, completedAt,
                habitId: habit.Id, payloadJson: payload, ct: ct);

            await db.SaveChangesAsync(ct);
        }

        return ToDto(habit, today);
    }

    public async Task<HabitDto> DeleteLogAsync(Guid userId, Guid habitId, DateOnly date, CancellationToken ct)
    {
        var habit = await FindActiveAsync(userId, habitId, ct);
        var today = await TodayAsync(userId, ct);

        var log = habit.Logs.FirstOrDefault(l => l.Date == date);
        if (log is not null)
        {
            db.HabitLogs.Remove(log); // відмітки видаляються фізично; клієнт отримує актуальний список у складі звички
            habit.ServerModifiedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return ToDto(habit, today);
    }

    private async Task<Habit> FindActiveAsync(Guid userId, Guid habitId, CancellationToken ct) =>
        await db.Habits.Include(h => h.Logs)
            .FirstOrDefaultAsync(h => h.Id == habitId && h.UserId == userId && !h.IsDeleted, ct)
        ?? throw new AppException(StatusCodes.Status404NotFound, "Habit not found.");

    private async Task<DateOnly> TodayAsync(Guid userId, CancellationToken ct) =>
        UserTime.Today(await userTime.GetZoneAsync(userId, ct), DateTime.UtcNow);

    private static HabitDto ToDto(Habit h, DateOnly today)
    {
        var streaks = h.IsDeleted
            ? new HabitStreaks(0, 0)
            : HabitStreakCalculator.Calculate(
                h.FrequencyType, h.DaysOfWeekMask, h.TimesPerWeek, h.Logs.Select(l => l.Date), today);

        IReadOnlyList<HabitLogDto> logs = h.IsDeleted
            ? Array.Empty<HabitLogDto>()
            : h.Logs.OrderBy(l => l.Date).Select(l => new HabitLogDto(l.Date, l.CompletedAtUtc)).ToList();

        return new HabitDto(
            h.Id, h.Title, h.Description, h.FrequencyType, h.DaysOfWeekMask, h.TimesPerWeek,
            h.ReminderTime, h.ColorHex, h.Icon, h.CategoryId,
            streaks.Current, streaks.Best, logs,
            h.UpdatedAtUtc, h.ServerModifiedAtUtc, h.IsDeleted);
    }
}