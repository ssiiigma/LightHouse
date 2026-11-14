using Lighthouse.Mobile.Models;
using Lighthouse.Mobile.ViewModels;

namespace Lighthouse.Mobile.Views;

public partial class HabitsPage : ContentPage
{
    private readonly HabitsViewModel _viewModel;

    public HabitsPage(HabitsViewModel viewModel)
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

    private async void OnCardTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is HabitCard card)
        {
            await _viewModel.OpenAsync(card);
        }
    }

    private async void OnMarkTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is HabitCard card)
        {
            await _viewModel.ToggleTodayAsync(card);
        }
    }
}