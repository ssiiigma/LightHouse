using Lighthouse.Mobile.ViewModels;

namespace Lighthouse.Mobile.Views;

public partial class BackupsPage : ContentPage
{
    private readonly BackupsViewModel _viewModel;

    public BackupsPage(BackupsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    private async void OnRestoreClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is BackupItem item)
        {
            await _viewModel.RestoreAsync(item);
        }
    }

    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is BackupItem item)
        {
            await _viewModel.DeleteAsync(item);
        }
    }
}