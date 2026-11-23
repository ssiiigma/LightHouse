using Lighthouse.Mobile.ViewModels;

namespace Lighthouse.Mobile.Views;

public partial class CalendarPage : ContentPage
{
    private readonly CalendarViewModel _viewModel;

    public CalendarPage(CalendarViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (Window is not null)
        {
            Window.Activated += OnWindowActivated; // повернення з браузера після входу Google
        }

        await _viewModel.RefreshAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (Window is not null)
        {
            Window.Activated -= OnWindowActivated;
        }
    }

    private async void OnWindowActivated(object? sender, EventArgs e) => await _viewModel.RefreshAsync();
}