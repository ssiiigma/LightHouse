namespace Lighthouse.Mobile.Controls;

/// <summary>Вибір часу без AM/PM: годинник 00–23 і хвилини 00–59, зі стрілками вгору/вниз біля кожного списку.</summary>
public sealed class TimeInputView : ContentView
{
    private static readonly List<string> Hours = Enumerable.Range(0, 24).Select(i => i.ToString("00")).ToList();
    private static readonly List<string> Minutes = Enumerable.Range(0, 60).Select(i => i.ToString("00")).ToList();

    public static readonly BindableProperty TimeProperty = BindableProperty.Create(
        nameof(Time), typeof(TimeSpan?), typeof(TimeInputView), null, BindingMode.TwoWay,
        propertyChanged: (bindable, _, _) => ((TimeInputView)bindable).SyncFromTime());

    private readonly Picker _hour;
    private readonly Picker _minute;
    private bool _updating;

    public TimeInputView()
    {
        _hour = CreatePicker(Hours);
        _minute = CreatePicker(Minutes);
        _hour.SelectedIndexChanged += (_, _) => OnPickerChanged();
        _minute.SelectedIndexChanged += (_, _) => OnPickerChanged();

        var grid = new Grid { ColumnSpacing = 6, RowSpacing = 4, HorizontalOptions = LayoutOptions.Center };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        Place(grid, MakeArrow("▲", () => Step(hours: -1)), 0, 0);
        Place(grid, Frame(_hour), 0, 1);
        Place(grid, MakeArrow("▼", () => Step(hours: 1)), 0, 2);

        Place(grid, new Label
        {
            Text = ":", FontSize = 24, FontFamily = "DMSansBold",
            HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center
        }, 1, 1);

        Place(grid, MakeArrow("▲", () => Step(minutes: -1)), 2, 0);
        Place(grid, Frame(_minute), 2, 1);
        Place(grid, MakeArrow("▼", () => Step(minutes: 1)), 2, 2);

        Content = grid;
        SyncFromTime();
    }

    public TimeSpan? Time
    {
        get => (TimeSpan?)GetValue(TimeProperty);
        set => SetValue(TimeProperty, value);
    }

    private void Step(int hours = 0, int minutes = 0)
    {
        var current = Time ?? TimeSpan.Zero;
        Time = new TimeSpan((current.Hours + hours + 24) % 24, (current.Minutes + minutes + 60) % 60, 0);
    }

    private void OnPickerChanged()
    {
        if (_updating)
        {
            return;
        }

        Time = new TimeSpan(Math.Max(0, _hour.SelectedIndex), Math.Max(0, _minute.SelectedIndex), 0);
    }

    private void SyncFromTime()
    {
        var time = Time ?? TimeSpan.Zero;
        _updating = true;
        _hour.SelectedIndex = time.Hours;
        _minute.SelectedIndex = time.Minutes;
        _updating = false;
    }

    private static Picker CreatePicker(List<string> items) => new()
    {
        ItemsSource = items,
        FontSize = 24,
        FontFamily = "DMSansSemibold",
        TextColor = Color.FromArgb("#F5EDD8"),
        HorizontalTextAlignment = TextAlignment.Center,
        WidthRequest = 70
    };

    private static Border Frame(View content) => new()
    {
        Content = content,
        BackgroundColor = Color.FromArgb("#1A2540"),
        Stroke = Color.FromArgb("#0FF5EDD8"),
        StrokeThickness = 1,
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
        Padding = 0
    };

    private static Button MakeArrow(string glyph, Action action)
    {
        var button = new Button
        {
            Text = glyph,
            FontSize = 13,
            Padding = 0,
            MinimumHeightRequest = 30,
            HeightRequest = 32,
            WidthRequest = 70,
            CornerRadius = 10,
            BackgroundColor = Color.FromArgb("#1A2540"),
            TextColor = Color.FromArgb("#E8B84B")
        };
        button.Clicked += (_, _) => action();
        return button;
    }

    private static void Place(Grid grid, View view, int column, int row)
    {
        Grid.SetColumn(view, column);
        Grid.SetRow(view, row);
        grid.Children.Add(view);
    }
}