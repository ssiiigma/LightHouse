using System.Collections.ObjectModel;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lighthouse.Mobile.Localization;
using Lighthouse.Mobile.Models;
using Lighthouse.Mobile.Services.Tasks;
using Lighthouse.Shared.Enums;

namespace Lighthouse.Mobile.ViewModels;

public sealed partial class SubtaskItem : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private bool _isDone;
}

/// <summary>Форма створення й редагування завдання. Нове завдання відкривається без параметра, наявне — з id.</summary>
public sealed partial class TaskEditViewModel(ITaskSvc tasks, LocalizationService loc)
    : ObservableObject, IQueryAttributable
{
    private Guid _id = Guid.NewGuid(); // для нового завдання ідентифікатор генерується одразу (повторне збереження безпечне)
    private Guid? _pendingId;
    private DateTime? _completedAtUtc;
    private bool _initialized;
    private bool _loading;
    private CategoryModel _noCategory = new();

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    // Властивості дати й часу nullable: у MAUI 10 DatePicker.Date і TimePicker.Time теж можуть бути nullable.
    [ObservableProperty]
    private bool _hasDeadline;

    [ObservableProperty]
    private DateTime? _deadlineDate = DateTime.Today;

    [ObservableProperty]
    private TimeSpan? _deadlineTime = new TimeSpan(18, 0, 0);

    [ObservableProperty]
    private bool _hasReminder;

    [ObservableProperty]
    private DateTime? _reminderDate = DateTime.Today;

    [ObservableProperty]
    private TimeSpan? _reminderTime = new TimeSpan(9, 0, 0);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLow), nameof(IsMedium), nameof(IsHigh), nameof(IsCritical))]
    private TaskPriority _priority = TaskPriority.Medium;

    [ObservableProperty]
    private string _estimate = string.Empty;

    [ObservableProperty]
    private CategoryModel? _selectedCategory;

    [ObservableProperty]
    private string _newCategoryName = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageTitle), nameof(IsExisting))]
    private bool _isNew = true;

    public ObservableCollection<CategoryModel> Categories { get; } = [];
    public ObservableCollection<SubtaskItem> Subtasks { get; } = [];

    public string PageTitle => IsNew ? loc["Form_NewTask"] : loc["Form_EditTask"];
    public bool IsExisting => !IsNew;
    public bool IsLow => Priority == TaskPriority.Low;
    public bool IsMedium => Priority == TaskPriority.Medium;
    public bool IsHigh => Priority == TaskPriority.High;
    public bool IsCritical => Priority == TaskPriority.Critical;

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
        _noCategory = new CategoryModel { Id = Guid.Empty, Name = loc["Form_NoCategory"] };
        await ReloadCategoriesAsync(selectId: null);

        if (_pendingId is { } id)
        {
            await LoadAsync(id);
        }
    }

    partial void OnHasReminderChanged(bool value)
    {
        if (_loading || !value)
        {
            return;
        }

        var basis = HasDeadline ? Combine(DeadlineDate, DeadlineTime).AddHours(-1) : DateTime.Now.AddHours(1);
        ReminderDate = basis.Date;
        ReminderTime = basis.TimeOfDay;
    }

    [RelayCommand]
    private void SetPriority(TaskPriority priority) => Priority = priority;

    [RelayCommand]
    private void AddSubtask() => Subtasks.Add(new SubtaskItem());

    public void RemoveSubtask(SubtaskItem item) => Subtasks.Remove(item);

    [RelayCommand]
    private Task GoBackAsync() => Shell.Current.GoToAsync("..");

    [RelayCommand]
    private async Task AddCategoryAsync()
    {
        var name = NewCategoryName.Trim();
        if (name.Length == 0)
        {
            return;
        }

        var id = await tasks.SaveCategoryAsync(name);
        NewCategoryName = string.Empty;
        await ReloadCategoriesAsync(id);
    }

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

        int? minutes = null;
        if (!string.IsNullOrWhiteSpace(Estimate))
        {
            if (!int.TryParse(Estimate.Trim(), out var parsed) || parsed is < 1 or > 1440)
            {
                ErrorMessage = loc["Err_EstimateInvalid"];
                return;
            }

            minutes = parsed;
        }

        var model = new TaskModel
        {
            Id = _id,
            Title = title,
            Description = Description,
            DeadlineUtc = HasDeadline ? ToUtc(DeadlineDate, DeadlineTime) : null,
            Priority = Priority,
            CategoryId = SelectedCategory is { } category && category.Id != Guid.Empty ? category.Id : null,
            ReminderAtUtc = HasReminder ? ToUtc(ReminderDate, ReminderTime) : null,
            CompletedAtUtc = _completedAtUtc,
            EstimatedMinutes = minutes,
            Subtasks = Subtasks
                .Where(s => !string.IsNullOrWhiteSpace(s.Title))
                .Select(s => new SubtaskModel { Id = s.Id, Title = s.Title, IsDone = s.IsDone })
                .ToList()
        };

        await tasks.SaveAsync(model);
        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        var id = _id;
        await tasks.DeleteAsync(id);
        await Shell.Current.GoToAsync("..");

        var snackbar = Snackbar.Make(
            loc["Task_Deleted"], () => _ = tasks.UndoDeleteAsync(id), loc["Btn_Undo"], TimeSpan.FromSeconds(5));
        await snackbar.Show();
    }

    private async Task LoadAsync(Guid id)
    {
        var model = await tasks.GetAsync(id);
        if (model is null)
        {
            await Shell.Current.GoToAsync("..");
            return;
        }

        _loading = true;
        try
        {
            _id = id;
            IsNew = false;
            _completedAtUtc = model.CompletedAtUtc;

            Title = model.Title;
            Description = model.Description ?? string.Empty;
            Priority = model.Priority;
            Estimate = model.EstimatedMinutes?.ToString() ?? string.Empty;

            if (model.DeadlineUtc is { } deadline)
            {
                var local = ToLocal(deadline);
                DeadlineDate = local.Date;
                DeadlineTime = local.TimeOfDay;
                HasDeadline = true;
            }

            if (model.ReminderAtUtc is { } reminder)
            {
                var local = ToLocal(reminder);
                ReminderDate = local.Date;
                ReminderTime = local.TimeOfDay;
                HasReminder = true;
            }

            SelectedCategory = Categories.FirstOrDefault(c => c.Id == model.CategoryId) ?? _noCategory;

            Subtasks.Clear();
            foreach (var subtask in model.Subtasks)
            {
                Subtasks.Add(new SubtaskItem { Id = subtask.Id, Title = subtask.Title, IsDone = subtask.IsDone });
            }
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task ReloadCategoriesAsync(Guid? selectId)
    {
        var current = selectId ?? SelectedCategory?.Id;

        Categories.Clear();
        Categories.Add(_noCategory);
        foreach (var category in await tasks.GetCategoriesAsync())
        {
            Categories.Add(category);
        }

        SelectedCategory = Categories.FirstOrDefault(c => c.Id == current) ?? _noCategory;
    }

    private static DateTime Combine(DateTime? date, TimeSpan? time) =>
        (date ?? DateTime.Today).Date + (time ?? TimeSpan.Zero);

    private static DateTime ToUtc(DateTime? date, TimeSpan? time) =>
        DateTime.SpecifyKind(Combine(date, time), DateTimeKind.Local).ToUniversalTime();

    private static DateTime ToLocal(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
}