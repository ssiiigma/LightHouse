using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lighthouse.Mobile.Localization;
using Lighthouse.Mobile.Models;
using Lighthouse.Mobile.Services.Habits;
using Lighthouse.Shared.Enums;

namespace Lighthouse.Mobile.ViewModels;

public sealed partial class DayChip : ObservableObject
{
    public int Index { get; init; }
    public string Label { get; init; } = string.Empty;

    [ObservableProperty]
    private bool _isSelected;
}

public sealed partial class ColorChip : ObservableObject
{
    public string Hex { get; init; } = "#C8922A";
    public Color Color => Color.FromArgb(Hex);

    [ObservableProperty]
    private bool _isSelected;
}

public sealed partial class HabitEditViewModel(IHabitSvc habits, LocalizationService loc)
    : ObservableObject, IQueryAttributable
{
    private static readonly string[] Palette = ["#38B2AC", "#E8864A", "#805AD5", "#5B9BD5", "#E05C7B", "#4CAF7D", "#E8B84B"];

    private Guid _id = Guid.NewGuid(); // повторне збереження безпечне (upsert за Id)
    private Guid? _pendingId;
    private bool _initialized;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDaily), nameof(IsSpecific), nameof(IsTimes))]
    private HabitFrequencyType _frequency = HabitFrequencyType.Daily;

    [ObservableProperty]
    private string _timesPerWeekText = "3";

    [ObservableProperty]
    private bool _hasReminder;

    [ObservableProperty]
    private TimeSpan? _reminderTime = new TimeSpan(8, 0, 0);

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageTitle), nameof(IsExisting))]
    private bool _isNew = true;

    public ObservableCollection<DayChip> Days { get; } = [];
    public ObservableCollection<ColorChip> Colors { get; } = [];

    public string PageTitle => IsNew ? loc["Habit_New"] : loc["Habit_Edit"];
    public bool IsExisting => !IsNew;
    public bool IsDaily => Frequency == HabitFrequencyType.Daily;
    public bool IsSpecific => Frequency == HabitFrequencyType.SpecificDays;
    public bool IsTimes => Frequency == HabitFrequencyType.TimesPerWeek;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var value) && Guid.TryParse(value?.ToString(), out var id))
        {
            _pendingId = id;
            if (_initialized)
            {
                _ = LoadAsync(id);
            }
        }
    }

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        var culture = CultureInfo.CurrentCulture;
        var monday = new DateTime(2024, 1, 1); // понеділок

        for (var i = 0; i < 7; i++)
        {
            Days.Add(new DayChip { Index = i, Label = monday.AddDays(i).ToString("ddd", culture) });
        }

        foreach (var hex in Palette)
        {
            Colors.Add(new ColorChip { Hex = hex });
        }

        Colors[0].IsSelected = true;

        if (_pendingId is { } id)
        {
            await LoadAsync(id);
        }
    }

    public void ToggleDay(DayChip chip) => chip.IsSelected = !chip.IsSelected;

    public void SelectColor(ColorChip chip)
    {
        foreach (var color in Colors)
        {
            color.IsSelected = color == chip;
        }
    }

    [RelayCommand]
    private void SetFrequency(HabitFrequencyType frequency) => Frequency = frequency;

    [RelayCommand]
    private Task GoBackAsync() => Shell.Current.GoToAsync("..");

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = null;
        var title = Title.Trim();
        if (title.Length == 0)
        {
            ErrorMessage = loc["Err_TitleRequired"];
            return;
        }

        var mask = Days.Where(d => d.IsSelected).Sum(d => 1 << d.Index);
        if (Frequency == HabitFrequencyType.SpecificDays && mask == 0)
        {
            ErrorMessage = loc["Err_HabitDays"];
            return;
        }

        int? times = null;
        if (Frequency == HabitFrequencyType.TimesPerWeek)
        {
            if (!int.TryParse(TimesPerWeekText.Trim(), out var parsed) || parsed is < 1 or > 7)
            {
                ErrorMessage = loc["Err_HabitTimes"];
                return;
            }

            times = parsed;
        }

        await habits.SaveAsync(new HabitModel
        {
            Id = _id,
            Title = title,
            Description = Description,
            FrequencyType = Frequency,
            DaysOfWeekMask = mask,
            TimesPerWeek = times,
            ReminderMinutes = HasReminder && ReminderTime is { } time ? (int)time.TotalMinutes : null,
            ColorHex = (Colors.FirstOrDefault(c => c.IsSelected) ?? Colors[0]).Hex
        });

        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        var confirmed = await Shell.Current.DisplayAlert(
            loc["Habit_DeleteTitle"], loc["Habit_DeleteConfirm"], loc["Btn_Delete"], loc["Btn_Cancel"]);
        if (!confirmed)
        {
            return;
        }

        await habits.DeleteAsync(_id);
        await Shell.Current.GoToAsync("..");
    }

    private async Task LoadAsync(Guid id)
    {
        var model = await habits.GetAsync(id);
        if (model is null)
        {
            await Shell.Current.GoToAsync("..");
            return;
        }

        _id = id;
        IsNew = false;
        Title = model.Title;
        Description = model.Description ?? string.Empty;
        Frequency = model.FrequencyType;
        TimesPerWeekText = (model.TimesPerWeek ?? 3).ToString(CultureInfo.InvariantCulture);

        foreach (var day in Days)
        {
            day.IsSelected = ((model.DaysOfWeekMask >> day.Index) & 1) == 1;
        }

        if (model.ReminderMinutes is { } minutes)
        {
            ReminderTime = TimeSpan.FromMinutes(minutes);
            HasReminder = true;
        }

        var chip = Colors.FirstOrDefault(c => string.Equals(c.Hex, model.ColorHex, StringComparison.OrdinalIgnoreCase));
        if (chip is not null)
        {
            SelectColor(chip);
        }
    }
}