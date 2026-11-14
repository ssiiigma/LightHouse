using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lighthouse.Mobile.Localization;
using Lighthouse.Mobile.Models;
using Lighthouse.Mobile.Services.Auth;
using Lighthouse.Mobile.Services.Sync;
using Lighthouse.Mobile.Services.Tasks;
using Lighthouse.Shared.Enums;

namespace Lighthouse.Mobile.ViewModels;

/// <summary>Група завдань за пріоритетом (заголовок із кольоровою точкою й лічильником).</summary>
public sealed class TaskGroup(string title, Color dotColor, IEnumerable<TaskModel> items) : List<TaskModel>(items)
{
    public string Title { get; } = title;
    public Color DotColor { get; } = dotColor;
}

public sealed partial class TasksViewModel(ITaskSvc tasks, ISyncSvc sync, IAuth auth, LocalizationService loc)
    : ObservableObject
{
    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _dateText = string.Empty;

    [ObservableProperty]
    private string _progressText = "0 / 0";

    [ObservableProperty]
    private double _progress;

    public ObservableCollection<TaskGroup> Groups { get; } = [];

    public async Task AttachAsync()
    {
        Detach(); // без подвійної підписки
        tasks.Changed += OnDataChanged;
        sync.DataChanged += OnDataChanged;
        sync.StatusChanged += OnStatusChanged;
        auth.SessionExpired += OnSessionExpired;
        loc.PropertyChanged += OnLanguageChanged;

        await tasks.RecoverAsync();
        sync.Start();
        await LoadAsync();
        UpdateStatus(sync.Status);
    }

    public void Detach()
    {
        tasks.Changed -= OnDataChanged;
        sync.DataChanged -= OnDataChanged;
        sync.StatusChanged -= OnStatusChanged;
        auth.SessionExpired -= OnSessionExpired;
        loc.PropertyChanged -= OnLanguageChanged;
    }

    public Task ToggleAsync(TaskModel task) => tasks.SetCompletedAsync(task.Id, !task.IsCompleted);

    public Task OpenAsync(TaskModel task) =>
        Shell.Current.GoToAsync($"{nameof(Views.TaskEditPage)}?id={task.Id}");

    [RelayCommand]
    private Task NewTaskAsync() => Shell.Current.GoToAsync(nameof(Views.TaskEditPage));

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
        var now = DateTime.Now;
        var today = now.Date;
        var endOfToday = today.AddDays(1);

        var all = await tasks.GetTasksAsync();
        var categories = (await tasks.GetCategoriesAsync()).ToDictionary(c => c.Id);

        // Денний прогрес: завдання з дедлайном до кінця сьогодні (разом із простроченими) + виконані сьогодні.
        var visible = new List<TaskModel>();
        int done = 0, planned = 0;
        foreach (var task in all)
        {
            DateTime? completed = task.CompletedAtUtc is { } c ? ToLocal(c) : null;
            DateTime? deadline = task.DeadlineUtc is { } d ? ToLocal(d) : null;

            if (completed is { } completedLocal)
            {
                if (completedLocal.Date != today)
                {
                    continue; // старі виконані не показуємо (лишаються в аналітиці)
                }

                done++;
                planned++;
            }
            else if (deadline is { } due && due < endOfToday)
            {
                planned++;
            }

            Decorate(task, deadline, categories, now, today, culture);
            visible.Add(task);
        }

        var ordered = visible
            .OrderBy(t => t.IsCompleted)
            .ThenBy(t => t.DeadlineUtc ?? DateTime.MaxValue)
            .ThenBy(t => t.Title)
            .ToList();

        Groups.Clear();
        AddGroup("Group_Critical", "#E05C5C", ordered.Where(t => t.Priority == TaskPriority.Critical));
        AddGroup("Group_High", "#E8A23A", ordered.Where(t => t.Priority == TaskPriority.High));
        AddGroup("Group_Normal", "#5B9BD5", ordered.Where(t => t.Priority is TaskPriority.Medium or TaskPriority.Low));

        Progress = planned == 0 ? 0 : (double)done / planned;
        ProgressText = $"{done} / {planned}";

        var date = now.ToString("dddd, d MMMM", culture);
        DateText = char.ToUpper(date[0], culture) + date[1..];
    }

    private void AddGroup(string titleKey, string colorHex, IEnumerable<TaskModel> items)
    {
        var list = items.ToList();
        if (list.Count > 0)
        {
            Groups.Add(new TaskGroup(loc[titleKey], Color.FromArgb(colorHex), list));
        }
    }

    private void Decorate(
        TaskModel task, DateTime? deadline, Dictionary<Guid, CategoryModel> categories,
        DateTime now, DateTime today, CultureInfo culture)
    {
        if (task.CategoryId is { } categoryId && categories.TryGetValue(categoryId, out var category))
        {
            task.CategoryName = category.Name;
        }

        if (task.IsCompleted)
        {
            task.DueText = loc["Due_Completed"];
            return;
        }

        if (deadline is not { } due)
        {
            return;
        }

        var time = due.ToString("t", culture);
        task.DueText = due.Date == today ? loc.Get("Due_Today", time)
            : due.Date == today.AddDays(1) ? loc.Get("Due_Tomorrow", time)
            : due.ToString("d MMM, t", culture);

        if (due < now)
        {
            task.IsOverdue = true;
            task.DueText = loc["Due_Overdue"] + " · " + task.DueText;
        }
    }

    private static DateTime ToLocal(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();

    private void OnDataChanged() => MainThread.BeginInvokeOnMainThread(async () => await LoadAsync());

    private void OnStatusChanged(SyncStatus status) => MainThread.BeginInvokeOnMainThread(() => UpdateStatus(status));

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e) =>
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            UpdateStatus(sync.Status);
            await LoadAsync();
        });

    private void OnSessionExpired() => MainThread.BeginInvokeOnMainThread(async () =>
    {
        Detach();
        await Shell.Current.GoToAsync("//welcome");
    });

    private void UpdateStatus(SyncStatus status) => StatusText = status switch
    {
        SyncStatus.Syncing => loc["Sync_Syncing"],
        SyncStatus.Offline => loc["Sync_Offline"],
        SyncStatus.Error => loc["Sync_Error"],
        _ => string.Empty
    };
}