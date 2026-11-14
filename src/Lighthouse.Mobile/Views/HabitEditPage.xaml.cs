using Lighthouse.Mobile.ViewModels;

namespace Lighthouse.Mobile.Views;

public partial class HabitEditPage : ContentPage
{
    private readonly HabitEditViewModel _viewModel;

    public HabitEditPage(HabitEditViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.InitializeAsync();
    }

    private void OnDayTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is DayChip chip)
        {
            _viewModel.ToggleDay(chip);
        }
    }

    private void OnColorTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is ColorChip chip)
        {
            _viewModel.SelectColor(chip);
        }
    }
}