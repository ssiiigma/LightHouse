using System.Text.Json;
using Lighthouse.Api.Data;
using Lighthouse.Api.Infrastructure;
using Lighthouse.Api.Services.Behavior;
using Lighthouse.Core.Entities;
using Lighthouse.Shared.Contracts.Tasks;
using Lighthouse.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Lighthouse.Api.Services.Tasks;

public sealed class TaskService(LighthouseDbContext db, IBehaviorRecorder behavior) : ITaskService
{
    public async Task<IReadOnlyList<TaskDto>> GetAsync(Guid userId, DateTime? modifiedSinceUtc, CancellationToken ct)
    {
        var query = db.Tasks.AsNoTracking().Include(t => t.Subtasks).Where(t => t.UserId == userId);

        query = SyncTime.ToUtc(modifiedSinceUtc) is { } since
            ? query.Where(t => t.ServerModifiedAtUtc > since)   // дельта: разом із «надгробками» видалених
            : query.Where(t => !t.IsDeleted);                   // перше завантаження

        var items = await query.OrderBy(t => t.ServerModifiedAtUtc).ToListAsync(ct);
        return items.Select(ToDto).ToList();
    }

    public async Task<TaskDto> UpsertAsync(Guid userId, Guid id, UpsertTaskRequest request, CancellationToken ct)
    {
        if (request.CategoryId is { } categoryId &&
            !await db.Categories.AnyAsync(c => c.Id == categoryId && c.UserId == userId && !c.IsDeleted, ct))
        {
            throw new AppException(StatusCodes.Status400BadRequest, "Category not found.");
        }

        var task = await db.Tasks.Include(t => t.Subtasks).FirstOrDefaultAsync(t => t.Id == id, ct);

        // Чужий запис маскуємо під «не існує», щоб не розкривати наявність ідентифікатора.
        if (task is not null && task.UserId != userId)
        {
            throw new AppException(StatusCodes.Status404NotFound, "Task not found.");
        }

        var now = DateTime.UtcNow;
        var editedAt = SyncTime.ClampToNow(request.ClientUpdatedAtUtc, now);
        var isNew = task is null;

        if (task is null)
        {
            task = new TaskItem { Id = id, UserId = userId, CreatedAtUtc = now };
            db.Tasks.Add(task);
        }
        else if (task.UpdatedAtUtc > editedAt)
        {
            return ToDto(task);
        }

        // Стан «до» — для визначення переходів, що породжують поведінкові події (повторна відправка подій не дублює).
        var previousDeadline = task.DeadlineUtc;
        var wasCompleted = task.CompletedAtUtc is not null;

        task.Title = request.Title.Trim();
        task.Description = request.Description;
        task.DeadlineUtc = SyncTime.ToUtc(request.DeadlineUtc);
        task.Priority = request.Priority;
        task.CategoryId = request.CategoryId;
        task.ReminderAtUtc = SyncTime.ToUtc(request.ReminderAtUtc);
        task.CompletedAtUtc = SyncTime.ToUtc(request.CompletedAtUtc);
        task.EstimatedMinutes = request.EstimatedMinutes;
        task.IsDeleted = false;
        task.UpdatedAtUtc = editedAt;
        task.ServerModifiedAtUtc = now;

        SyncSubtasks(task, request.Subtasks, now);

        if (isNew)
        {
            await behavior.RecordAsync(userId, BehaviorEventType.TaskCreated, editedAt, taskId: id, ct: ct);
        }

        if (!wasCompleted && task.CompletedAtUtc is { } completedAt)
        {
            await behavior.RecordAsync(userId, BehaviorEventType.TaskCompleted,
                SyncTime.ClampToNow(completedAt, now), taskId: id, ct: ct);
        }

        // «Відкладання» = перенесення дедлайну існуючого завдання на пізніший момент.
        if (previousDeadline is { } oldDeadline && task.DeadlineUtc is { } newDeadline && newDeadline > oldDeadline)
        {
            var payload = JsonSerializer.Serialize(new
            {
                PreviousDeadlineUtc = SyncTime.ToUtc(oldDeadline),
                NewDeadlineUtc = newDeadline
            });
            await behavior.RecordAsync(userId, BehaviorEventType.TaskPostponed, editedAt,
                taskId: id, payloadJson: payload, ct: ct);
        }

        await db.SaveChangesAsync(ct);
        return ToDto(task);
    }

    public async Task DeleteAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (task is null)
        {
            return; // видалення ідемпотентне
        }

        if (task.UserId != userId)
        {
            throw new AppException(StatusCodes.Status404NotFound, "Task not found.");
        }

        if (task.IsDeleted)
        {
            return;
        }

        var now = DateTime.UtcNow;
        task.IsDeleted = true; // архів; фізичне видалення через 14 днів — окрема фонова задача (UC-09)
        task.UpdatedAtUtc = now;
        task.ServerModifiedAtUtc = now;

        // Видалення невиконаного завдання вважається його пропуском.
        if (task.CompletedAtUtc is null)
        {
            await behavior.RecordAsync(userId, BehaviorEventType.TaskSkipped, now, taskId: id, ct: ct);
        }

        await db.SaveChangesAsync(ct);
    }

    private void SyncSubtasks(TaskItem task, IReadOnlyList<SubtaskDto> incoming, DateTime now)
    {
        var incomingIds = incoming.Select(s => s.Id).ToHashSet();

        foreach (var removed in task.Subtasks.Where(s => !incomingIds.Contains(s.Id)).ToList())
        {
            db.Subtasks.Remove(removed);
        }

        for (var order = 0; order < incoming.Count; order++)
        {
            var dto = incoming[order];
            var existing = task.Subtasks.FirstOrDefault(s => s.Id == dto.Id);

            if (existing is null)
            {
                // Add() явно, щоб EF не вважав сутність із заданим ключем уже існуючою.
                db.Subtasks.Add(new Subtask
                {
                    Id = dto.Id,
                    TaskItemId = task.Id,
                    Title = dto.Title.Trim(),
                    IsDone = dto.IsDone,
                    SortOrder = order,
                    UpdatedAtUtc = now,
                    ServerModifiedAtUtc = now
                });
            }
            else
            {
                existing.Title = dto.Title.Trim();
                existing.IsDone = dto.IsDone;
                existing.SortOrder = order;
                existing.UpdatedAtUtc = now;
                existing.ServerModifiedAtUtc = now;
            }
        }
    }

    private static TaskDto ToDto(TaskItem t) => new(
        t.Id,
        t.Title,
        t.Description,
        t.DeadlineUtc,
        t.Priority,
        t.CategoryId,
        t.ReminderAtUtc,
        t.CompletedAtUtc,
        t.EstimatedMinutes,
        t.Subtasks.OrderBy(s => s.SortOrder).Select(s => new SubtaskDto(s.Id, s.Title, s.IsDone)).ToList(),
        t.UpdatedAtUtc,
        t.ServerModifiedAtUtc,
        t.IsDeleted);
}