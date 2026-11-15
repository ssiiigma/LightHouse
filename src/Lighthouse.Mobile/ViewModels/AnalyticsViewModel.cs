using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lighthouse.Mobile.Controls;
using Lighthouse.Mobile.Localization;
using Lighthouse.Mobile.Models;
using Lighthouse.Mobile.Services.Analytics;
using Lighthouse.Mobile.Services.Api;
using Lighthouse.Mobile.Services.Habits;
using Lighthouse.Mobile.Services.Sync;
using Lighthouse.Mobile.Services.Tasks;
using Lighthouse.Shared.Analytics;

namespace Lighthouse.Mobile.ViewModels;

public sealed partial class AnalyticsViewModel(
    IAnalyticsSvc analytics, ITaskSvc tasks, IHabitSvc habits, ISyncSvc sync,
    IConnectivity connectivity, LocalizationService loc) : ObservableObject
{
    private static readonly Color Green = Color.FromArgb("#4CAF7D");
    private static readonly Color Red = Color.FromArgb("#E05C5C");
    private static readonly Color Muted = Color.FromArgb("#8CF5EDD8");
    private static readonly string[] Palette = ["#E8B84B", "#4CAF7D", "#5B9BD5", "#805AD5"];
    private const string OtherColor = "#6E6885";

    private int _loadVersion;
    private DateRange _range;
    private bool _isExporting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDay), nameof(IsWeek), nameof(IsMonth), nameof(IsYear))]
    private AnalyticsPeriod _period = AnalyticsPeriod.Week;

    [ObservableProperty] private KpiCard _kpi1 = new();
    [ObservableProperty] private KpiCard _kpi2 = new();
    [ObservableProperty] private KpiCard _kpi3 = new();
    [ObservableProperty] private KpiCard _kpi4 = new();
    [ObservableProperty] private string _rangeText = string.Empty;
    [ObservableProperty] private string _insightText = string.Empty;
    [ObservableProperty] private bool _hasCategories;
    [ObservableProperty] private string? _exportMessage;

    public bool IsDay => Period == AnalyticsPeriod.Day;
    public bool IsWeek => Period == AnalyticsPeriod.Week;
    public bool IsMonth => Period == AnalyticsPeriod.Month;
    public bool IsYear => Period == AnalyticsPeriod.Year;

    public BarChartDrawable Bars { get; } = new();
    public DonutDrawable Donut { get; } = new();
    public HeatmapDrawable Heatmap { get; } = new();
    public ObservableCollection<LegendItem> Legend { get; } = [];

    /// <summary>Дані діаграм змінились: сторінка перемальовує GraphicsView.</summary>
    public event Action? ChartsChanged;

    public async Task AttachAsync()
    {
        Detach();
        tasks.Changed += OnDataChanged;
        habits.Changed += OnDataChanged;
        sync.DataChanged += OnDataChanged;
        loc.PropertyChanged += OnLanguageChanged;
        await LoadAsync();
    }

    public void Detach()
    {
        tasks.Changed -= OnDataChanged;
        habits.Changed -= OnDataChanged;
        sync.DataChanged -= OnDataChanged;
        loc.PropertyChanged -= OnLanguageChanged;
    }

    partial void OnPeriodChanged(AnalyticsPeriod value) => _ = LoadAsync();

    [RelayCommand]
    private void SetPeriod(AnalyticsPeriod period) => Period = period;

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (_isExporting)
        {
            return;
        }

        var choice = await Shell.Current.CurrentPage.DisplayActionSheet(
            loc["Export_Title"], loc["Btn_Cancel"], null, "PDF", "CSV");
        if (choice is not ("PDF" or "CSV"))
        {
            return;
        }

        ExportMessage = null;
        if (connectivity.NetworkAccess != NetworkAccess.Internet)
        {
            ExportMessage = loc["Export_NoNetwork"];
            return;
        }

        _isExporting = true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var file = await analytics.DownloadReportAsync(choice.ToLowerInvariant(), _range, timeout.Token);
            await Share.Default.RequestAsync(new ShareFileRequest(loc["Export_Title"], new ShareFile(file)));
        }
        catch (OperationCanceledException)
        {
            ExportMessage = loc["Export_Timeout"];
        }
        catch (Exception)
        {
            ExportMessage = loc["Export_Failed"];
        }
        finally
        {
            _isExporting = false;
        }
    }

    private async Task LoadAsync()
    {
        var version = ++_loadVersion;
        var data = await analytics.GetAsync(Period);
        if (version != _loadVersion)
        {
            return; // застарілий результат (вкладку вже змінили)
        }

        Apply(data);
    }

    private void Apply(AnalyticsData data)
    {
        var r = data.Result;
        var culture = CultureInfo.CurrentCulture;
        _range = r.Range;
        RangeText = FormatRange(r.Range, culture);

        var comparison = ComparisonText();
        var rate = DeltaPoints(r.CompletionRate, r.PreviousCompletionRate, comparison);
        var count = DeltaPercent(r.Completed, r.PreviousCompleted, comparison);
        var regularity = DeltaPoints(r.HabitRegularity, r.PreviousHabitRegularity, comparison);

        Kpi1 = new KpiCard { Value = Percent(r.CompletionRate), Label = loc["Kpi_Rate"], Delta = rate.Text, DeltaColor = rate.Color };
        Kpi2 = new KpiCard { Value = r.Completed.ToString(culture), Label = loc["Kpi_Completed"], Delta = count.Text, DeltaColor = count.Color };
        Kpi3 = new KpiCard { Value = Percent(r.HabitRegularity), Label = loc["Kpi_Habits"], Delta = regularity.Text, DeltaColor = regularity.Color };
        Kpi4 = new KpiCard
        {
            Value = r.CurrentStreak.ToString(culture),
            Label = loc["Kpi_Streak"],
            Delta = loc.Get("Kpi_Record", r.BestStreak),
            DeltaColor = Muted
        };

        Bars.Values = r.Buckets.Select(v => (float)v).ToList();
        Bars.Labels = BucketLabels(r, culture);
        Bars.CurrentIndex = r.CurrentBucket;

        BuildCategories(data, culture);

        var maxCount = r.HeatmapCounts.Max();
        Heatmap.Levels = r.HeatmapCounts
            .Select(c => c < 0 ? -1 : c == 0 ? 0 : Math.Clamp((int)Math.Ceiling(c * 4.0 / Math.Max(1, maxCount)), 1, 4))
            .ToArray();
        Heatmap.RowLabels =
        [
            culture.DateTimeFormat.GetAbbreviatedDayName(DayOfWeek.Monday), "",
            culture.DateTimeFormat.GetAbbreviatedDayName(DayOfWeek.Wednesday), "",
            culture.DateTimeFormat.GetAbbreviatedDayName(DayOfWeek.Friday), "", ""
        ];

        InsightText = r.ProductiveDay is { } day && r.ProductiveHourBlock is { } block
            ? loc.Get("Insight_Template", culture.DateTimeFormat.GetDayName(day), $"{block * 4:00}:00–{block * 4 + 4:00}:00")
            : loc["Insight_NoData"];

        ChartsChanged?.Invoke();
    }

    private void BuildCategories(AnalyticsData data, CultureInfo culture)
    {
        var r = data.Result;
        Legend.Clear();
        HasCategories = r.Completed > 0;

        var segments = new List<(float, Color)>();
        var shown = r.Categories.Take(4).ToList();
        for (var i = 0; i < shown.Count; i++)
        {
            var share = shown[i];
            CategoryModel? category = null;
            var known = share.CategoryId is { } id && data.Categories.TryGetValue(id, out category);
            var color = Color.FromArgb(known && !string.IsNullOrEmpty(category!.ColorHex) ? category.ColorHex : Palette[i]);

            segments.Add(((float)share.Fraction, color));
            Legend.Add(new LegendItem
            {
                Name = known ? category!.Name : loc["Cat_None"],
                PercentText = $"{Math.Round(share.Fraction * 100):0}%",
                Color = color
            });
        }

        var rest = r.Categories.Skip(4).Sum(c => c.Fraction);
        if (rest > 0)
        {
            var color = Color.FromArgb(OtherColor);
            segments.Add(((float)rest, color));
            Legend.Add(new LegendItem { Name = loc["Cat_Other"], PercentText = $"{Math.Round(rest * 100):0}%", Color = color });
        }

        Donut.Segments = segments;
        Donut.CenterText = r.Completed.ToString(culture);
    }

    private List<string> BucketLabels(AnalyticsResult r, CultureInfo culture)
    {
        var labels = new List<string>();
        switch (r.BucketKind)
        {
            case BucketKind.HourBlock:
                for (var i = 0; i < r.Buckets.Length; i++)
                {
                    labels.Add($"{i * 4:00}");
                }

                break;
            case BucketKind.Day when Period == AnalyticsPeriod.Week:
                for (var i = 0; i < r.Buckets.Length; i++)
                {
                    labels.Add(culture.DateTimeFormat.GetAbbreviatedDayName(r.Range.From.AddDays(i).DayOfWeek));
                }

                break;
            case BucketKind.Day:
                for (var i = 0; i < r.Buckets.Length; i++)
                {
                    var day = i + 1;
                    labels.Add(day == 1 || day % 5 == 0 ? day.ToString(culture) : string.Empty);
                }

                break;
            default:
                for (var i = 0; i < r.Buckets.Length; i++)
                {
                    labels.Add(culture.DateTimeFormat.GetAbbreviatedMonthName(i + 1).TrimEnd('.'));
                }

                break;
        }

        return labels;
    }

    private static string FormatRange(DateRange range, CultureInfo culture) => range.Days switch
    {
        1 => range.From.ToString("d MMMM", culture),
        7 => $"{range.From.ToString("d MMM", culture)} – {range.To.ToString("d MMM", culture)}",
        >= 365 => range.From.Year.ToString(culture),
        _ => char.ToUpper(range.From.ToString("MMMM yyyy", culture)[0], culture) + range.From.ToString("MMMM yyyy", culture)[1..]
    };

    private string ComparisonText() => Period switch
    {
        AnalyticsPeriod.Day => loc["Cmp_Day"],
        AnalyticsPeriod.Week => loc["Cmp_Week"],
        AnalyticsPeriod.Month => loc["Cmp_Month"],
        _ => loc["Cmp_Year"]
    };

    private static string Percent(double? fraction) => fraction is { } f ? $"{Math.Round(f * 100):0}%" : "—";

    private (string Text, Color Color) DeltaPercent(int current, int previous, string comparison)
    {
        if (previous == 0)
        {
            return current == 0 ? ("—", Muted) : ($"↑ {loc["Delta_New"]} {comparison}", Green);
        }

        return FormatDelta(Math.Round((current - previous) * 100.0 / previous), "%", comparison);
    }

    private (string Text, Color Color) DeltaPoints(double? current, double? previous, string comparison) =>
        current is { } c && previous is { } p
            ? FormatDelta(Math.Round((c - p) * 100), " " + loc["Unit_Pp"], comparison)
            : ("—", Muted);

    private static (string Text, Color Color) FormatDelta(double delta, string unit, string comparison) => delta switch
    {
        0 => ($"= {comparison}", Muted),
        > 0 => ($"↑ +{delta:0}{unit} {comparison}", Green),
        _ => ($"↓ {delta:0}{unit} {comparison}", Red)
    };

    private void OnDataChanged() => MainThread.BeginInvokeOnMainThread(async () => await LoadAsync());

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e) =>
        MainThread.BeginInvokeOnMainThread(async () => await LoadAsync());
}