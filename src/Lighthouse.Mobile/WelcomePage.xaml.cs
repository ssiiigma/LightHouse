using System.Diagnostics;
using Lighthouse.Mobile.Controls;
using Lighthouse.Mobile.ViewModels;

namespace Lighthouse.Mobile;

public partial class WelcomePage : ContentPage
{
    private readonly WelcomeViewModel _viewModel;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private IDispatcherTimer? _timer;

    public WelcomePage(WelcomeViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        StartAnimation();
        await _viewModel.CheckSessionAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _timer?.Stop(); // без анімації, коли екран не видно (економія батареї)
    }

    private void StartAnimation()
    {
        if (_timer is null)
        {
            _timer = Dispatcher.CreateTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(40);
            _timer.Tick += (_, _) =>
            {
                if (Resources["Scene"] is LighthouseSceneDrawable scene)
                {
                    scene.Time = _clock.Elapsed.TotalSeconds;
                    SceneView.Invalidate();
                }
            };
        }

        _timer.Start();
    }
}