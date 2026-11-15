namespace Lighthouse.Mobile.Models;

public sealed class KpiCard
{
    public string Value { get; init; } = "—";
    public string Label { get; init; } = string.Empty;
    public string Delta { get; init; } = string.Empty;
    public Color DeltaColor { get; init; } = Colors.Gray;
}

public sealed class LegendItem
{
    public string Name { get; init; } = string.Empty;
    public string PercentText { get; init; } = string.Empty;
    public Color Color { get; init; } = Colors.Gray;
}