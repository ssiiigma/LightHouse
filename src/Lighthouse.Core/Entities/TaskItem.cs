using Lighthouse.Shared.Enums;

namespace Lighthouse.Core.Entities;

public class TaskItem : BaseEntity
{
    public Guid UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime? DeadlineUtc { get; set; }
    public TaskPriority Priority { get; set; } = TaskPriority.Medium;
    public Guid? CategoryId { get; set; }
    public DateTime? ReminderAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int? EstimatedMinutes { get; set; }

    public ICollection<Subtask> Subtasks { get; set; } = new List<Subtask>();
}
