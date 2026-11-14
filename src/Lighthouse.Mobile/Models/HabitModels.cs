using Lighthouse.Mobile.Data;
using Lighthouse.Shared.Enums;

namespace Lighthouse.Mobile.Models;

/// <summary>Звичка з відмітками (дані, без подання).</summary>
public sealed class HabitModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public HabitFrequencyType FrequencyType { get; set; } = HabitFrequencyType.Daily;
    public int DaysOfWeekMask { get; set; }
    public int? TimesPerWeek { get; set; }
    public int? ReminderMinutes { get; set; }
    public string? ColorHex { get; set; }
    public string? Icon { get; set; }
    public SyncState SyncState { get; set; }
    public List<DateOnly> LogDates { get; set; } = [];
}

/// <summary>Смужка тижня на картці звички.</summary>
public sealed class DotItem
{
    public Color Fill { get; init; } = Colors.Transparent;
    public Color Stroke { get; init; } = Colors.Transparent;
}

/// <summary>День у рядку тижня вгорі екрана.</summary>
public sealed class WeekDayItem
{
    public string DayName { get; init; } = string.Empty;
    public string Mark { get; init; } = string.Empty;
    public Color Background { get; init; } = Colors.Transparent;
    public Color Foreground { get; init; } = Colors.White;
    public Color Stroke { get; init; } = Colors.Transparent;
}

/// <summary>Картка звички на екрані.</summary>
public sealed class HabitCard
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string StreakText { get; init; } = string.Empty;
    public string WeekText { get; init; } = string.Empty;
    public string Letter { get; init; } = string.Empty;
    public Color Accent { get; init; } = Colors.Gold;
    public Color AccentSoft { get; init; } = Colors.Transparent;
    public bool IsDoneToday { get; init; }
    public string MarkText => IsDoneToday ? "✓" : "+";
    public Color MarkBackground => IsDoneToday ? Color.FromArgb("#4CAF7D") : Color.FromArgb("#1A2540");
    public Color MarkForeground => IsDoneToday ? Colors.White : Color.FromArgb("#8CF5EDD8");
    public bool IsUnsynced { get; init; }
    public List<DotItem> Dots { get; init; } = [];
}