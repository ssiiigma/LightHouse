using Lighthouse.Mobile.ViewModels;

namespace Lighthouse.Mobile.Views;

public partial class TaskEditPage : ContentPage
{
    private readonly TaskEditViewModel _viewModel;

    public TaskEditPage(TaskEditViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.InitializeAsync();
    }

    private void OnRemoveSubtask(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is SubtaskItem item)
        {
            _viewModel.RemoveSubtask(item);
        }
    }
}