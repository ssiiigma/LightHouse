using Lighthouse.Mobile.ViewModels;

namespace Lighthouse.Mobile.Views;

public partial class CategoriesPage : ContentPage
{
    private readonly CategoriesViewModel _viewModel;

    public CategoriesPage(CategoriesViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is CategoryItem item)
        {
            await _viewModel.DeleteAsync(item);
        }
    }
}