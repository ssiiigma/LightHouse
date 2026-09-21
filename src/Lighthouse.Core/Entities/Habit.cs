using Lighthouse.Shared.Enums;

namespace Lighthouse.Core.Entities;

public class Habit : BaseEntity
{
    public Guid UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public HabitFrequencyType FrequencyType { get; set; } = HabitFrequencyType.Daily;

    /// <summary>Біти 0..6 = дні тижня (пн..нд) для режиму SpecificDays.</summary>
    public int DaysOfWeekMask { get; set; }

    /// <summary>Кількість разів на тиждень для режиму TimesPerWeek.</summary>
    public int? TimesPerWeek { get; set; }
    public TimeOnly? ReminderTime { get; set; }
    public string? ColorHex { get; set; }
    public string? Icon { get; set; }
    public Guid? CategoryId { get; set; }

    public ICollection<HabitLog> Logs { get; set; } = new List<HabitLog>();
}
