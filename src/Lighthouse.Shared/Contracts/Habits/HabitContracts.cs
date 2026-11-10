using Lighthouse.Shared.Enums;

namespace Lighthouse.Shared.Contracts.Habits;

/// <summary>Відмітка виконання звички за день (унікальна пара звичка + дата).</summary>
public sealed record HabitLogDto(DateOnly Date, DateTime CompletedAtUtc);

/// <summary>
/// Звичка з відмітками та серіями. Серії обчислюються сервером з відміток
/// (на пристрої клієнт рахує їх самостійно тим самим алгоритмом для роботи офлайн).
/// </summary>
public sealed record HabitDto(
    Guid Id,
    string Title,
    string? Description,
    HabitFrequencyType FrequencyType,
    int DaysOfWeekMask,
    int? TimesPerWeek,
    TimeOnly? ReminderTime,
    string? ColorHex,
    string? Icon,
    Guid? CategoryId,
    int CurrentStreak,
    int BestStreak,
    IReadOnlyList<HabitLogDto> Logs,
    DateTime UpdatedAtUtc,
    DateTime ServerModifiedAtUtc,
    bool IsDeleted);

/// <summary>Тіло PUT /api/habits/{id} (upsert, Id генерує клієнт).</summary>
public sealed record UpsertHabitRequest(
    string Title,
    string? Description,
    HabitFrequencyType FrequencyType,
    int DaysOfWeekMask,
    int? TimesPerWeek,
    TimeOnly? ReminderTime,
    string? ColorHex,
    string? Icon,
    Guid? CategoryId,
    DateTime ClientUpdatedAtUtc);

/// <summary>Необов'язкове тіло PUT /api/habits/{id}/logs/{yyyy-MM-dd}; без тіла береться поточний час сервера.</summary>
public sealed record UpsertHabitLogRequest(DateTime? CompletedAtUtc);