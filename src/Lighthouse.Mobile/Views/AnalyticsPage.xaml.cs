using Lighthouse.Mobile.ViewModels;

namespace Lighthouse.Mobile.Views;

public partial class AnalyticsPage : ContentPage
{
    private readonly AnalyticsViewModel _viewModel;

    public AnalyticsPage(AnalyticsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.ChartsChanged += InvalidateCharts;
        await _viewModel.AttachAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.ChartsChanged -= InvalidateCharts;
        _viewModel.Detach();
    }

    private void InvalidateCharts()
    {
        BarsView.Invalidate();
        HeatmapView.Invalidate();
        DonutView.Invalidate();
    }
}