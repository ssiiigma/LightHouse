using CommunityToolkit.Maui.Behaviors;
using Lighthouse.Mobile.Localization;
using Microsoft.Maui.Controls.Shapes;

namespace Lighthouse.Mobile.Controls;

/// <summary>
/// Нижня панель вкладок із підсвічуванням активної: золота іконка із сяйвом, золотий підпис і промінь, що повільно пульсує.
/// Активна вкладка визначається за сторінкою, у шаблон якої вбудовано панель; пульсація йде лише поки сторінка видима.
/// </summary>
public sealed class BottomNavBar : ContentView
{
    private const string PulseName = "nav-pulse";
    private static readonly Color ActiveColor = Color.FromArgb("#E8B84B");
    private static readonly Color InactiveColor = Color.FromArgb("#6E6885");

    private sealed record Tab(string Route, string Icon, string TitleKey, string PageType);

    private sealed class TabView
    {
        public required Tab Tab { get; init; }
        public required IconTintColorBehavior Tint { get; init; }
        public required Label Title { get; init; }
        public required BoxView Beam { get; init; }
        public required Border Glow { get; init; }
    }

    private static readonly Tab[] Tabs =
    [
        new("tasks", "tab_tasks", "Tab_Tasks", "TasksPage"),
        new("habits", "tab_habits", "Tab_Habits", "HabitsPage"),
        new("analytics", "tab_analytics", "Tab_Analytics", "AnalyticsPage"),
        new("profile", "tab_profile", "Tab_Profile", "ProfilePage")
    ];

    private readonly List<TabView> _views = [];
    private readonly LocalizationService _loc = LocalizationService.Instance;
    private Page? _host;
    private string? _active;

    public BottomNavBar()
    {
        var grid = new Grid { Padding = new Thickness(0, 8, 0, 14), BackgroundColor = Color.FromArgb("#F20A0E1A") };
        foreach (var _ in Tabs)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        }

        for (var i = 0; i < Tabs.Length; i++)
        {
            var tab = Tabs[i];

            var tint = new IconTintColorBehavior { TintColor = InactiveColor };
            var icon = new Image
            {
                Source = tab.Icon, WidthRequest = 24, HeightRequest = 24,
                HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center
            };
            icon.Behaviors.Add(tint);

            var glow = new Border
            {
                WidthRequest = 48, HeightRequest = 32, StrokeThickness = 0, Opacity = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 16 },
                Background = new RadialGradientBrush
                {
                    Center = new Point(0.5, 0.5),
                    Radius = 0.6,
                    GradientStops =
                    [
                        new GradientStop(Color.FromArgb("#73E8B84B"), 0f),
                        new GradientStop(Color.FromArgb("#00E8B84B"), 1f)
                    ]
                }
            };

            var iconHost = new Grid { WidthRequest = 48, HeightRequest = 32, HorizontalOptions = LayoutOptions.Center };
            iconHost.Children.Add(glow);
            iconHost.Children.Add(icon);

            var title = new Label
            {
                FontSize = 10, FontFamily = "DMSansMedium", HorizontalOptions = LayoutOptions.Center,
                TextColor = InactiveColor
            };
            var beam = new BoxView
            {
                WidthRequest = 30, HeightRequest = 3, CornerRadius = 2, Color = ActiveColor,
                HorizontalOptions = LayoutOptions.Center, Opacity = 0
            };

            var cell = new VerticalStackLayout
            {
                Spacing = 3, BackgroundColor = Color.FromArgb("#01000000"),
                Children = { iconHost, title, beam }
            };
            var tap = new TapGestureRecognizer();
            var route = tab.Route;
            tap.Tapped += async (_, _) => await GoAsync(route);
            cell.GestureRecognizers.Add(tap);

            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
            _views.Add(new TabView { Tab = tab, Tint = tint, Title = title, Beam = beam, Glow = glow });
        }

        Content = new VerticalStackLayout
        {
            Spacing = 0,
            Children = { new BoxView { HeightRequest = 1, Color = Color.FromArgb("#33C8922A") }, grid }
        };

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async Task GoAsync(string route)
    {
        if (route == _active || Shell.Current is null)
        {
            return;
        }

        await Shell.Current.GoToAsync($"//{route}");
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        _host = FindHost();
        _active = Tabs.FirstOrDefault(t => t.PageType == _host?.GetType().Name)?.Route;
        _loc.PropertyChanged += OnLanguageChanged;
        if (_host is not null)
        {
            _host.Appearing += OnHostAppearing;
            _host.Disappearing += OnHostDisappearing;
        }

        ApplyTitles();
        ApplyColors();
        SetPulse(_host is not null && Shell.Current?.CurrentPage == _host);
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        _loc.PropertyChanged -= OnLanguageChanged;
        if (_host is not null)
        {
            _host.Appearing -= OnHostAppearing;
            _host.Disappearing -= OnHostDisappearing;
        }

        SetPulse(false);
    }

    private void OnHostAppearing(object? sender, EventArgs e) => SetPulse(true);

    private void OnHostDisappearing(object? sender, EventArgs e) => SetPulse(false);

    private void OnLanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
        MainThread.BeginInvokeOnMainThread(ApplyTitles);

    private Page? FindHost()
    {
        Element? element = this;
        while (element is not null)
        {
            if (element is Page page)
            {
                return page;
            }

            element = element.Parent;
        }

        return null;
    }

    private void ApplyTitles()
    {
        foreach (var view in _views)
        {
            view.Title.Text = _loc[view.Tab.TitleKey];
        }
    }

    private void ApplyColors()
    {
        foreach (var view in _views)
        {
            var color = view.Tab.Route == _active ? ActiveColor : InactiveColor;
            view.Tint.TintColor = color;
            view.Title.TextColor = color;
        }
    }

    private void SetPulse(bool on)
    {
        foreach (var view in _views)
        {
            view.Beam.AbortAnimation(PulseName);
            var active = view.Tab.Route == _active;
            view.Beam.Opacity = active ? 0.85 : 0;
            view.Glow.Opacity = active ? 0.85 : 0;
            if (on && active)
            {
                StartPulse(view);
            }
        }
    }

    private static void StartPulse(TabView view)
    {
        void Apply(double v)
        {
            view.Beam.Opacity = v;
            view.Glow.Opacity = 0.55 + 0.45 * v;
        }

        var animation = new Animation();
        animation.Add(0, 0.5, new Animation(Apply, 0.3, 1.0, Easing.SinInOut));
        animation.Add(0.5, 1, new Animation(Apply, 1.0, 0.3, Easing.SinInOut));
        animation.Commit(view.Beam, PulseName, length: 2400, repeat: () => true);
    }
}