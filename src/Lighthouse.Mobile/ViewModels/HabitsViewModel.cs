using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lighthouse.Mobile.Localization;
using Lighthouse.Mobile.Models;
using Lighthouse.Mobile.Services.Habits;
using Lighthouse.Mobile.Services.Sync;
using Lighthouse.Shared.Enums;
using Lighthouse.Shared.Habits;

namespace Lighthouse.Mobile.ViewModels;

public sealed partial class HabitsViewModel(IHabitSvc habits, ISyncSvc sync, LocalizationService loc) : ObservableObject
{
    private static readonly Color CardBackground = Color.FromArgb("#1A2540");
    private static readonly Color Gold = Color.FromArgb("#C8922A");
    private static readonly Color GoldLight = Color.FromArgb("#E8B84B");
    private static readonly Color Cream = Color.FromArgb("#F5EDD8");
    private static readonly Color Dim = Color.FromArgb("#40F5EDD8");
    private static readonly Color Navy = Color.FromArgb("#0A0E1A");
    private static readonly Color MissedBackground = Color.FromArgb("#33E05C5C");
    private static readonly Color MissedForeground = Color.FromArgb("#B3E05C5C");

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private string _weekRateText = "—";

    [ObservableProperty]
    private string _todayText = "0/0";

    [ObservableProperty]
    private string _bestText = "0";

    public ObservableCollection<HabitCard> Cards { get; } = [];
    public ObservableCollection<WeekDayItem> WeekDays { get; } = [];

    public async Task AttachAsync()
    {
        Detach();
        habits.Changed += OnDataChanged;
        sync.DataChanged += OnDataChanged;
        loc.PropertyChanged += OnLanguageChanged;
        await LoadAsync();
    }

    public void Detach()
    {
        habits.Changed -= OnDataChanged;
        sync.DataChanged -= OnDataChanged;
        loc.PropertyChanged -= OnLanguageChanged;
    }

    public Task ToggleTodayAsync(HabitCard card) =>
        habits.ToggleAsync(card.Id, DateOnly.FromDateTime(DateTime.Now));

    public Task OpenAsync(HabitCard card) =>
        Shell.Current.GoToAsync($"{nameof(Views.HabitEditPage)}?id={card.Id}");

    [RelayCommand]
    private Task NewHabitAsync() => Shell.Current.GoToAsync(nameof(Views.HabitEditPage));

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        try
        {
            await sync.SyncNowAsync();
            await LoadAsync();
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private async Task LoadAsync()
    {
        var culture = CultureInfo.CurrentCulture;
        var today = DateOnly.FromDateTime(DateTime.Now);
        var todayIndex = DayIndex(today);
        var weekStart = today.AddDays(-todayIndex);
        var all = await habits.GetHabitsAsync();

        var cards = new List<HabitCard>();
        var best = 0;
        int expectedTotal = 0, doneTotal = 0, scheduledToday = 0, doneToday = 0;

        foreach (var habit in all)
        {
            var logs = habit.LogDates.ToHashSet();
            var streaks = HabitStreakCalculator.Calculate(
                habit.FrequencyType, habit.DaysOfWeekMask, habit.TimesPerWeek, habit.LogDates, today);
            best = Math.Max(best, streaks.Best);

            var weekLogs = Enumerable.Range(0, 7).Count(d => logs.Contains(weekStart.AddDays(d)));
            var target = WeeklyTarget(habit);
            var expectedSoFar = ExpectedSoFar(habit, todayIndex, target);
            if (expectedSoFar > 0)
            {
                expectedTotal += expectedSoFar;
                doneTotal += Math.Min(weekLogs, expectedSoFar);
            }

            var isScheduledToday = ScheduledOn(habit, todayIndex, includeTimes: true);
            var isDoneToday = logs.Contains(today);
            if (isScheduledToday)
            {
                scheduledToday++;
                if (isDoneToday)
                {
                    doneToday++;
                }
            }

            var accent = Color.FromArgb(string.IsNullOrEmpty(habit.ColorHex) ? "#C8922A" : habit.ColorHex);
            cards.Add(new HabitCard
            {
                Id = habit.Id,
                Title = habit.Title,
                Letter = habit.Title.Length > 0 ? habit.Title[..1].ToUpper(culture) : "?",
                Accent = accent,
                AccentSoft = accent.WithAlpha(0.18f),
                StreakText = loc.Get("Habit_Streak", streaks.Current),
                WeekText = loc.Get("Habit_WeekCount", Math.Min(weekLogs, target), target),
                IsDoneToday = isDoneToday,
                IsUnsynced = habit.SyncState != Data.SyncState.Synced,
                Dots = Enumerable.Range(0, 7).Select(d =>
                {
                    var date = weekStart.AddDays(d);
                    return new DotItem
                    {
                        Fill = logs.Contains(date) ? accent : CardBackground,
                        Stroke = date == today && !logs.Contains(date) ? accent : Colors.Transparent
                    };
                }).ToList()
            });
        }

        WeekDays.Clear();
        for (var d = 0; d < 7; d++)
        {
            WeekDays.Add(BuildWeekDay(all, weekStart.AddDays(d), d, today, culture));
        }

        Cards.Clear();
        foreach (var card in cards)
        {
            Cards.Add(card);
        }

        WeekRateText = expectedTotal == 0 ? "—" : $"{Math.Round(doneTotal * 100.0 / expectedTotal):0}%";
        TodayText = $"{doneToday}/{scheduledToday}";
        BestText = best.ToString(culture);
    }

    /// <summary>
    /// День тижня: ✓ — виконано все заплановане (щодня й за днями; звички «N разів на тиждень» не враховуються),
    /// ✗ — минулий день із пропуском, золота рамка — сьогодні, інакше число місяця.
    /// </summary>
    private WeekDayItem BuildWeekDay(IReadOnlyList<HabitModel> all, DateOnly date, int index, DateOnly today, CultureInfo culture)
    {
        var scheduled = all.Where(h => ScheduledOn(h, index, includeTimes: false)).ToList();
        var allDone = scheduled.Count > 0 && scheduled.All(h => h.LogDates.Contains(date));
        var name = culture.DateTimeFormat.GetAbbreviatedDayName(date.DayOfWeek).ToUpper(culture);

        string mark = date.Day.ToString(culture);
        var background = CardBackground;
        var foreground = Dim;
        var stroke = Colors.Transparent;

        if (scheduled.Count > 0 && date <= today && allDone)
        {
            mark = "✓";
            background = GoldLight;
            foreground = Navy;
        }
        else if (scheduled.Count > 0 && date < today)
        {
            mark = "✗";
            background = MissedBackground;
            foreground = MissedForeground;
        }

        if (date == today)
        {
            stroke = Gold;
            if (mark != "✓")
            {
                foreground = Cream;
            }
        }

        return new WeekDayItem { DayName = name, Mark = mark, Background = background, Foreground = foreground, Stroke = stroke };
    }

    private static int DayIndex(DateOnly date) => ((int)date.DayOfWeek + 6) % 7;

    private static bool ScheduledOn(HabitModel habit, int dayIndex, bool includeTimes) => habit.FrequencyType switch
    {
        HabitFrequencyType.Daily => true,
        HabitFrequencyType.SpecificDays => ((habit.DaysOfWeekMask >> dayIndex) & 1) == 1,
        _ => includeTimes
    };

    private static int WeeklyTarget(HabitModel habit) => habit.FrequencyType switch
    {
        HabitFrequencyType.Daily => 7,
        HabitFrequencyType.SpecificDays => Math.Max(1, Enumerable.Range(0, 7).Count(d => ((habit.DaysOfWeekMask >> d) & 1) == 1)),
        _ => Math.Clamp(habit.TimesPerWeek ?? 1, 1, 7)
    };

    private static int ExpectedSoFar(HabitModel habit, int todayIndex, int target) => habit.FrequencyType switch
    {
        HabitFrequencyType.Daily => todayIndex + 1,
        HabitFrequencyType.SpecificDays => Enumerable.Range(0, todayIndex + 1).Count(d => ((habit.DaysOfWeekMask >> d) & 1) == 1),
        _ => target
    };

    private void OnDataChanged() => MainThread.BeginInvokeOnMainThread(async () => await LoadAsync());

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e) =>
        MainThread.BeginInvokeOnMainThread(async () => await LoadAsync());
}