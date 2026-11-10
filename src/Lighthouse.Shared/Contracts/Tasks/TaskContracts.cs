using Lighthouse.Shared.Enums;

namespace Lighthouse.Shared.Contracts.Tasks;

public sealed record SubtaskDto(Guid Id, string Title, bool IsDone);

/// <summary>
/// Завдання, яке сервер повертає клієнту.
/// <see cref="UpdatedAtUtc"/> — момент редагування (для вирішення конфліктів «останній запис»),
/// <see cref="ServerModifiedAtUtc"/> — момент запису на сервері (курсор для pull-синхронізації).
/// </summary>
public sealed record TaskDto(
    Guid Id,
    string Title,
    string? Description,
    DateTime? DeadlineUtc,
    TaskPriority Priority,
    Guid? CategoryId,
    DateTime? ReminderAtUtc,
    DateTime? CompletedAtUtc,
    int? EstimatedMinutes,
    IReadOnlyList<SubtaskDto> Subtasks,
    DateTime UpdatedAtUtc,
    DateTime ServerModifiedAtUtc,
    bool IsDeleted);

/// <summary>
/// Тіло PUT /api/tasks/{id}: створення або оновлення (upsert). Ідентифікатор генерує клієнт (GUID),
/// тому офлайн-створені завдання можна безпечно повторно відправляти з черги.
/// </summary>
public sealed record UpsertTaskRequest(
    string Title,
    string? Description,
    DateTime? DeadlineUtc,
    TaskPriority Priority,
    Guid? CategoryId,
    DateTime? ReminderAtUtc,
    DateTime? CompletedAtUtc,
    int? EstimatedMinutes,
    IReadOnlyList<SubtaskDto> Subtasks,
    DateTime ClientUpdatedAtUtc);