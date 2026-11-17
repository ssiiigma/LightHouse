using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;

namespace Lighthouse.Mobile.Controls;

/// <summary>Інформаційна плашка: золота рамка, значок «i», текст і необов'язкова кнопка дії. Для повідомлень, що не є помилками.</summary>
public sealed class NoticeView : ContentView
{
    private static readonly Color Gold = Color.FromArgb("#E8B84B");
    private static readonly Color Navy = Color.FromArgb("#0A0E1A");

    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text), typeof(string), typeof(NoticeView), string.Empty,
        propertyChanged: (b, _, n) => ((NoticeView)b)._label.Text = n as string);

    public static readonly BindableProperty ActionTextProperty = BindableProperty.Create(
        nameof(ActionText), typeof(string), typeof(NoticeView), string.Empty,
        propertyChanged: (b, _, n) =>
        {
            var view = (NoticeView)b;
            view._action.Text = n as string;
            view._action.IsVisible = !string.IsNullOrEmpty(n as string);
        });

    public static readonly BindableProperty ActionCommandProperty = BindableProperty.Create(
        nameof(ActionCommand), typeof(ICommand), typeof(NoticeView), null,
        propertyChanged: (b, _, n) => ((NoticeView)b)._action.Command = n as ICommand);

    private readonly Label _label;
    private readonly Button _action;

    public NoticeView()
    {
        _label = new Label
        {
            FontSize = 12, LineHeight = 1.3, TextColor = Color.FromArgb("#F5EDD8"),
            VerticalOptions = LayoutOptions.Center
        };
        _action = new Button
        {
            FontSize = 12, FontFamily = "DMSansSemibold", HeightRequest = 34, Padding = new Thickness(14, 0),
            CornerRadius = 17, BackgroundColor = Gold, TextColor = Navy, IsVisible = false,
            VerticalOptions = LayoutOptions.Center
        };

        var icon = new Border
        {
            WidthRequest = 22, HeightRequest = 22, StrokeThickness = 0, BackgroundColor = Gold,
            StrokeShape = new RoundRectangle { CornerRadius = 11 }, VerticalOptions = LayoutOptions.Start,
            Content = new Label
            {
                Text = "i", FontFamily = "DMSansBold", FontSize = 13, TextColor = Navy,
                HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center
            }
        };

        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        Grid.SetColumn(_label, 1);
        Grid.SetColumn(_action, 2);
        grid.Children.Add(icon);
        grid.Children.Add(_label);
        grid.Children.Add(_action);

        Content = new Border
        {
            BackgroundColor = Color.FromArgb("#29C8922A"),
            Stroke = Color.FromArgb("#80C8922A"),
            StrokeThickness = 1,
            Padding = new Thickness(12, 10),
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Content = grid
        };
    }

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }

    public string ActionText { get => (string)GetValue(ActionTextProperty); set => SetValue(ActionTextProperty, value); }

    public ICommand? ActionCommand { get => (ICommand?)GetValue(ActionCommandProperty); set => SetValue(ActionCommandProperty, value); }
}