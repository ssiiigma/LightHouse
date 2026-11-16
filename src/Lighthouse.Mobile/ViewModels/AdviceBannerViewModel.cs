using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lighthouse.Mobile.Localization;
using Lighthouse.Mobile.Models;
using Lighthouse.Mobile.Services.AI;
using Lighthouse.Mobile.Services.Tasks;

namespace Lighthouse.Mobile.ViewModels;

/// <summary>Банер рекомендацій угорі екрана «Завдання»: показує одну пораду, тап розгортає, кнопки лише для порад, що вимагають дії.</summary>
public sealed partial class AdviceBannerViewModel(IAIClient ai, ITaskSvc tasks, LocalizationService loc)
    : ObservableObject
{
    private int _dirty;

    private readonly List<AdviceItem> _items = [];
    private bool _refreshing;
    private bool _busy;
    private string? _expandedFor;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible), nameof(Title), nameof(Summary), nameof(Detail),
        nameof(IsActionable), nameof(ShowHide), nameof(ApplyText), nameof(DismissText))]
    private AdviceItem? _current;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCollapsed), nameof(Chevron))]
    private bool _isExpanded;

    [ObservableProperty]
    private string _moreText = string.Empty;

    public bool IsVisible => Current is not null;
    public bool IsCollapsed => !IsExpanded;
    public string Chevron => IsExpanded ? "▴" : "▾";
    public string Title => Current?.Title ?? string.Empty;
    public string Summary => Current?.Summary ?? string.Empty;
    public string Detail => Current?.Detail ?? string.Empty;
    public bool IsActionable => Current?.IsActionable == true;
    public bool ShowHide => Current is { IsActionable: false };
    public string ApplyText => Current?.ApplyText ?? string.Empty;
    public string DismissText => Current?.DismissText ?? string.Empty;

    partial void OnCurrentChanged(AdviceItem? value)
    {
        if (value?.Id != _expandedFor)
        {
            IsExpanded = false;
        }
    }

    /// <summary>Оновлює поради. Якщо оновлення вже триває, після нього виконується ще одне (остання зміна даних не губиться).</summary>
    public async Task RefreshAsync(bool force = false)
    {
        if (_refreshing)
        {
            Volatile.Write(ref _dirty, 1);
            return;
        }

        _refreshing = true;
        try
        {
            do
            {
                Volatile.Write(ref _dirty, 0);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                SetItems(await ai.GetAdviceAsync(timeout.Token));
            }
            while (Volatile.Read(ref _dirty) == 1);
        }
        catch (Exception)
        {
            // Поради необов'язкові: помилка не повинна заважати роботі зі списком завдань.
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>Прибирає поради про завдання, яких більше немає серед відкритих (виконано або видалено).</summary>
    public void Prune(HashSet<Guid> openTaskIds)
    {
        var kept = _items.Where(i => i.TaskIds.Any(openTaskIds.Contains)).ToList();
        if (kept.Count != _items.Count)
        {
            SetItems(kept);
        }
    }

    [RelayCommand]
    private void Toggle()
    {
        if (Current is null)
        {
            return;
        }

        IsExpanded = !IsExpanded;
        _expandedFor = IsExpanded ? Current.Id : null;
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (_busy || Current is not { } item)
        {
            return;
        }

        _busy = true;
        try
        {
            switch (item.Kind)
            {
                case AdviceKind.Reschedule when item.ProposedReminderUtc is { } reminder:
                    await tasks.SetReminderAsync(item.TaskIds[0], reminder);
                    break;
                case AdviceKind.Defer:
                    await tasks.ShiftAsync(item.TaskIds, TimeSpan.FromDays(1));
                    break;
            }

            await ai.ResolveAsync(item, applied: true);
            Advance(item);
        }
        finally
        {
            _busy = false;
        }
    }

    [RelayCommand]
    private async Task DismissAsync()
    {
        if (_busy || Current is not { } item)
        {
            return;
        }

        _busy = true;
        try
        {
            await ai.ResolveAsync(item, applied: false);
            Advance(item);
        }
        finally
        {
            _busy = false;
        }
    }

    private void Advance(AdviceItem resolved)
    {
        _items.Remove(resolved);
        _expandedFor = null;
        Current = _items.FirstOrDefault();
        UpdateMore();
    }

    private void SetItems(IReadOnlyList<AdviceItem> items)
    {
        var currentId = Current?.Id;
        _items.Clear();
        _items.AddRange(items);
        Current = _items.FirstOrDefault(i => i.Id == currentId) ?? _items.FirstOrDefault();
        UpdateMore();
    }

    private void UpdateMore() =>
        MoreText = _items.Count > 1 ? loc.Get("Advice_More", _items.Count - 1) : string.Empty;
}