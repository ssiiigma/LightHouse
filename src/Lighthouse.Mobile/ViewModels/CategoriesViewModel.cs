using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lighthouse.Mobile.Localization;
using Lighthouse.Mobile.Services.Tasks;

namespace Lighthouse.Mobile.ViewModels;

public sealed class CategoryItem
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public Color Color { get; init; } = Colors.Gold;
}

public sealed partial class CategoriesViewModel(ITaskSvc tasks, LocalizationService loc) : ObservableObject
{
    [ObservableProperty]
    private string _newName = string.Empty;

    [ObservableProperty]
    private string? _infoMessage;

    [ObservableProperty]
    private bool _isEmpty;

    public ObservableCollection<CategoryItem> Items { get; } = [];

    partial void OnNewNameChanged(string value) => InfoMessage = null;

    public async Task LoadAsync()
    {
        await tasks.MergeDuplicateCategoriesAsync(); // зливає дублікати, створені раніше

        var list = await tasks.GetCategoriesAsync();
        Items.Clear();
        foreach (var category in list)
        {
            Items.Add(new CategoryItem
            {
                Id = category.Id,
                Name = category.Name,
                Color = Color.FromArgb(string.IsNullOrEmpty(category.ColorHex) ? "#C8922A" : category.ColorHex)
            });
        }

        IsEmpty = Items.Count == 0;
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        if (string.IsNullOrWhiteSpace(NewName))
        {
            return;
        }

        var result = await tasks.AddCategoryAsync(NewName);
        NewName = string.Empty;
        InfoMessage = result.Created ? null : loc["Cat_AlreadyExists"]; // не помилка, а повідомлення до відома
        await LoadAsync();
    }

    public async Task DeleteAsync(CategoryItem item)
    {
        var confirmed = await Shell.Current.DisplayAlertAsync(
            loc["Cat_DeleteTitle"], loc.Get("Cat_DeleteConfirm", item.Name), loc["Btn_Delete"], loc["Btn_Cancel"]);
        if (!confirmed)
        {
            return;
        }

        await tasks.DeleteCategoryAsync(item.Id);
        await LoadAsync();
    }

    [RelayCommand]
    private Task GoBackAsync() => Shell.Current.GoToAsync("..");
}