using System.Globalization;
using Lighthouse.Shared.Analytics;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Lighthouse.Api.Services.Reports;

internal sealed record ReportTask(DateTime CompletedLocal, string Title, string Category, string Priority);

internal sealed record ReportHabit(string Title, int CheckIns, int CurrentStreak, int BestStreak);

internal sealed record ReportData(
    string UserName, RangeSummary Summary, IReadOnlyDictionary<Guid, string> CategoryNames,
    IReadOnlyList<ReportTask> Tasks, IReadOnlyList<ReportHabit> Habits, DateTime GeneratedLocal);

internal static class ReportFonts
{
    public const string Family = "Noto Sans";
    private static readonly object Gate = new();
    private static bool _initialized;

    public static bool Registered { get; private set; }

    /// <summary>Реєструє ліцензію Community і шрифти з папки Fonts (потрібні для кирилиці). Викликається один раз.</summary>
    public static void EnsureInitialized(ILogger logger)
    {
        lock (Gate)
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            QuestPDF.Settings.License = LicenseType.Community;

            var found = 0;
            foreach (var file in new[] { "NotoSans-Regular.ttf", "NotoSans-Bold.ttf" })
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Fonts", file);
                if (!File.Exists(path))
                {
                    logger.LogWarning("Report font {File} was not found; Cyrillic text may not render correctly.", path);
                    continue;
                }

                FontManager.RegisterFont(new MemoryStream(File.ReadAllBytes(path)));
                found++;
            }

            Registered = found == 2;
        }
    }
}

/// <summary>PDF-звіт: показники, виконані за днями/місяцями, категорії, звички й перелік виконаних завдань.</summary>
internal static class ReportPdf
{
    private const string Gold = "#B8821F";
    private const string Ink = "#1A2540";
    private const string Muted = "#6B7280";
    private const string Line = "#D9D2C3";
    private const int MaxListedTasks = 200;
    private const int DailyBarsLimit = 62;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static byte[] Build(ReportData data, ReportStrings s) =>
        Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(32);
                page.DefaultTextStyle(style =>
                    (ReportFonts.Registered ? style.FontFamily(ReportFonts.Family) : style).FontSize(10).FontColor(Ink));

                page.Header().Column(column =>
                {
                    column.Item().Text("LIGHTHOUSE").FontSize(9).Bold().FontColor(Gold);
                    column.Item().Text(s.Title).FontSize(20).Bold();
                    column.Item().PaddingTop(4).Text(text =>
                    {
                        text.DefaultTextStyle(x => x.FontSize(9).FontColor(Muted));
                        text.Span($"{s.User}: {data.UserName}    ");
                        text.Span($"{s.Period}: {Date(data.Summary.Range.From)} – {Date(data.Summary.Range.To)}    ");
                        text.Span($"{s.Generated}: {data.GeneratedLocal.ToString("dd.MM.yyyy HH:mm", Inv)}");
                    });
                    column.Item().PaddingTop(6).LineHorizontal(1).LineColor(Line);
                });

                page.Content().PaddingTop(12).Column(column =>
                {
                    column.Spacing(16);
                    Summary(column, data.Summary, s);
                    Bars(column, data.Summary, s);
                    CategoryTable(column, data, s);
                    HabitTable(column, data, s);
                    TaskTable(column, data, s);
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.DefaultTextStyle(x => x.FontSize(8).FontColor(Muted));
                    text.Span($"Lighthouse · {s.Page} ");
                    text.CurrentPageNumber();
                    text.Span(" / ");
                    text.TotalPages();
                });
            });
        }).GeneratePdf();

    private static void Summary(ColumnDescriptor column, RangeSummary sum, ReportStrings s) =>
        column.Item().Row(row =>
        {
            row.Spacing(8);
            Card(row.RelativeItem(), Percent(sum.CompletionRate), s.CompletionRate);
            Card(row.RelativeItem(), sum.Completed.ToString(Inv), s.CompletedCount);
            Card(row.RelativeItem(), Percent(sum.HabitRegularity), s.HabitRegularity);
            Card(row.RelativeItem(), sum.BestStreak.ToString(Inv), s.BestStreak);
        });

    private static void Card(IContainer container, string value, string label) =>
        container.Border(1).BorderColor(Line).Padding(8).Column(c =>
        {
            c.Item().Text(value).FontSize(18).Bold();
            c.Item().Text(label).FontSize(8).FontColor(Muted);
        });

    private static void Bars(ColumnDescriptor column, RangeSummary sum, ReportStrings s)
    {
        var daily = sum.Range.Days <= DailyBarsLimit;
        var buckets = daily
            ? sum.Days.Select(d => (Label: d.Date.ToString("dd.MM", Inv), Count: d.Completed)).ToList()
            : sum.Days.GroupBy(d => (d.Date.Year, d.Date.Month))
                .Select(g => (Label: $"{g.Key.Year}-{g.Key.Month:00}", Count: g.Sum(d => d.Completed)))
                .ToList();

        column.Item().Column(section =>
        {
            section.Spacing(6);
            section.Item().Text(daily ? s.PerDay : s.PerMonth).FontSize(12).Bold().FontColor(Gold);

            var max = buckets.Count == 0 ? 0 : buckets.Max(b => b.Count);
            if (max == 0)
            {
                section.Item().Text(s.NoData).FontColor(Muted);
                return;
            }

            section.Item().Column(bars =>
            {
                bars.Spacing(2);
                foreach (var (label, count) in buckets)
                {
                    bars.Item().Row(r =>
                    {
                        r.ConstantItem(52).Text(label).FontSize(8).FontColor(Muted);
                        r.RelativeItem().PaddingVertical(1).Row(bar =>
                        {
                            if (count > 0)
                            {
                                bar.RelativeItem(count).Height(8).Background(Gold);
                            }

                            if (max > count)
                            {
                                bar.RelativeItem(max - count);
                            }
                        });
                        r.ConstantItem(24).AlignRight().Text(count.ToString(Inv)).FontSize(8);
                    });
                }
            });
        });
    }

    private static void CategoryTable(ColumnDescriptor column, ReportData data, ReportStrings s) =>
        column.Item().Column(section =>
        {
            section.Spacing(6);
            section.Item().Text(s.Categories).FontSize(12).Bold().FontColor(Gold);
            if (data.Summary.Categories.Count == 0)
            {
                section.Item().Text(s.NoData).FontColor(Muted);
                return;
            }

            section.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(4);
                    c.RelativeColumn(1);
                    c.RelativeColumn(1);
                });
                table.Header(h =>
                {
                    h.Cell().Element(HeaderCell).Text(s.ColCategory);
                    h.Cell().Element(HeaderCell).AlignRight().Text(s.ColCount);
                    h.Cell().Element(HeaderCell).AlignRight().Text(s.ColShare);
                });

                foreach (var share in data.Summary.Categories)
                {
                    var name = share.CategoryId is { } id && data.CategoryNames.TryGetValue(id, out var n) ? n : s.NoCategory;
                    table.Cell().Element(BodyCell).Text(name);
                    table.Cell().Element(BodyCell).AlignRight().Text(share.Count.ToString(Inv));
                    table.Cell().Element(BodyCell).AlignRight().Text(Percent(share.Fraction));
                }
            });
        });

    private static void HabitTable(ColumnDescriptor column, ReportData data, ReportStrings s) =>
        column.Item().Column(section =>
        {
            section.Spacing(6);
            section.Item().Text(s.Habits).FontSize(12).Bold().FontColor(Gold);
            if (data.Habits.Count == 0)
            {
                section.Item().Text(s.NoData).FontColor(Muted);
                return;
            }

            section.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(4);
                    c.RelativeColumn(1);
                    c.RelativeColumn(1);
                    c.RelativeColumn(1);
                });
                table.Header(h =>
                {
                    h.Cell().Element(HeaderCell).Text(s.ColHabit);
                    h.Cell().Element(HeaderCell).AlignRight().Text(s.ColCheckIns);
                    h.Cell().Element(HeaderCell).AlignRight().Text(s.ColCurrentStreak);
                    h.Cell().Element(HeaderCell).AlignRight().Text(s.ColBestStreak);
                });

                foreach (var habit in data.Habits)
                {
                    table.Cell().Element(BodyCell).Text(habit.Title);
                    table.Cell().Element(BodyCell).AlignRight().Text(habit.CheckIns.ToString(Inv));
                    table.Cell().Element(BodyCell).AlignRight().Text(habit.CurrentStreak.ToString(Inv));
                    table.Cell().Element(BodyCell).AlignRight().Text(habit.BestStreak.ToString(Inv));
                }
            });
        });

    private static void TaskTable(ColumnDescriptor column, ReportData data, ReportStrings s) =>
        column.Item().Column(section =>
        {
            section.Spacing(6);
            section.Item().Text(s.TasksSection).FontSize(12).Bold().FontColor(Gold);
            if (data.Tasks.Count == 0)
            {
                section.Item().Text(s.NoData).FontColor(Muted);
                return;
            }

            section.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(74);
                    c.RelativeColumn(5);
                    c.RelativeColumn(2);
                    c.RelativeColumn(2);
                });
                table.Header(h =>
                {
                    h.Cell().Element(HeaderCell).Text(s.ColDate);
                    h.Cell().Element(HeaderCell).Text(s.ColTask);
                    h.Cell().Element(HeaderCell).Text(s.ColCategory);
                    h.Cell().Element(HeaderCell).Text(s.ColPriority);
                });

                foreach (var task in data.Tasks.Take(MaxListedTasks))
                {
                    table.Cell().Element(BodyCell).Text(task.CompletedLocal.ToString("dd.MM.yyyy HH:mm", Inv)).FontSize(8);
                    table.Cell().Element(BodyCell).Text(task.Title);
                    table.Cell().Element(BodyCell).Text(task.Category).FontSize(9);
                    table.Cell().Element(BodyCell).Text(task.Priority).FontSize(9);
                }
            });

            if (data.Tasks.Count > MaxListedTasks)
            {
                section.Item().Text(string.Format(Inv, s.Truncated, MaxListedTasks, data.Tasks.Count))
                    .FontSize(8).FontColor(Muted);
            }
        });

    private static IContainer HeaderCell(IContainer container) =>
        container.BorderBottom(1).BorderColor(Gold).PaddingVertical(4).DefaultTextStyle(t => t.Bold().FontSize(9));

    private static IContainer BodyCell(IContainer container) =>
        container.BorderBottom(0.5f).BorderColor(Line).PaddingVertical(3);

    private static string Percent(double? fraction) => fraction is { } f ? $"{Math.Round(f * 100):0}%" : "—";

    private static string Date(DateOnly date) => date.ToString("dd.MM.yyyy", Inv);
}