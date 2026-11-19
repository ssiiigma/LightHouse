using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;

namespace Lighthouse.Mobile.Controls;

/// <summary>Кнопка «Продовжити з Google»: світла, з кольоровою літерою G (за рекомендаціями Google до оформлення).</summary>
public sealed class GoogleSignInButton : ContentView
{
    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text), typeof(string), typeof(GoogleSignInButton), string.Empty,
        propertyChanged: (b, _, n) => ((GoogleSignInButton)b)._label.Text = n as string);

    public static readonly BindableProperty CommandProperty = BindableProperty.Create(
        nameof(Command), typeof(ICommand), typeof(GoogleSignInButton), null);

    private readonly Label _label;

    public GoogleSignInButton()
    {
        _label = new Label
        {
            FontFamily = "DMSansSemibold", FontSize = 15, TextColor = Color.FromArgb("#1F1F1F"),
            VerticalOptions = LayoutOptions.Center
        };

        var content = new HorizontalStackLayout
        {
            Spacing = 12, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Image { Source = "google_g.png", WidthRequest = 20, HeightRequest = 20, VerticalOptions = LayoutOptions.Center },
                _label
            }
        };

        var border = new Border
        {
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#DADCE0"),
            StrokeThickness = 1,
            HeightRequest = 52,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Content = content
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            if (Command?.CanExecute(null) == true)
            {
                Command.Execute(null);
            }
        };
        border.GestureRecognizers.Add(tap);
        Content = border;
    }

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }

    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
}