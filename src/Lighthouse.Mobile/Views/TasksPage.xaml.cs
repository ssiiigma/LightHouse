using Lighthouse.Mobile.Models;
using Lighthouse.Mobile.ViewModels;

namespace Lighthouse.Mobile.Views;

public partial class TasksPage : ContentPage
{
    private readonly TasksViewModel _viewModel;

    public TasksPage(TasksViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.AttachAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.Detach();
    }

    private async void OnTaskTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is TaskModel task)
        {
            await _viewModel.OpenAsync(task);
        }
    }

    private async void OnToggleTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is TaskModel task)
        {
            await _viewModel.ToggleAsync(task);
        }
    }
}