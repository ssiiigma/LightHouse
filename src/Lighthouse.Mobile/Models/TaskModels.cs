using Lighthouse.Mobile.Data;
using Lighthouse.Shared.Enums;

namespace Lighthouse.Mobile.Models;

public sealed class SubtaskModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public bool IsDone { get; set; }
}

/// <summary>Завдання у вигляді, зручному для екранів (читання зі SQLite й редагування у формі).</summary>
public sealed class TaskModel
{
    private static readonly Color Red = Color.FromArgb("#E05C5C");
    private static readonly Color Amber = Color.FromArgb("#E8A23A");
    private static readonly Color Blue = Color.FromArgb("#5B9BD5");
    private static readonly Color Green = Color.FromArgb("#4CAF7D");
    private static readonly Color Muted = Color.FromArgb("#8CF5EDD8");

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime? DeadlineUtc { get; set; }
    public TaskPriority Priority { get; set; } = TaskPriority.Medium;
    public Guid? CategoryId { get; set; }
    public DateTime? ReminderAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int? EstimatedMinutes { get; set; }
    public List<SubtaskModel> Subtasks { get; set; } = [];
    public DateTime UpdatedAtUtc { get; set; }
    public SyncState SyncState { get; set; }

    // Подання (заповнюється моделлю екрана).
    public string? CategoryName { get; set; }
    public string DueText { get; set; } = string.Empty;
    public bool IsOverdue { get; set; }

    public bool IsCompleted => CompletedAtUtc is not null;
    public bool IsUnsynced => SyncState != SyncState.Synced;
    public bool HasCategory => !string.IsNullOrEmpty(CategoryName);
    public bool HasDue => DueText.Length > 0;

    public Color AccentColor => Priority switch
    {
        TaskPriority.Critical => Red,
        TaskPriority.High => Amber,
        _ => Blue
    };

    public Brush CheckBorderBrush => new SolidColorBrush(IsCompleted ? Green : AccentColor);
    public Color CheckFillColor => IsCompleted ? Green : Colors.Transparent;
    public Color DueColor => IsOverdue ? Red : Muted;
    public double CardOpacity => IsCompleted ? 0.55 : 1.0;
    public TextDecorations TitleDecorations => IsCompleted ? TextDecorations.Strikethrough : TextDecorations.None;
}

public sealed class CategoryModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ColorHex { get; set; }
}