using Microsoft.Maui.Graphics;

namespace Lighthouse.Mobile.Controls;

/// <summary>Стовпчики «виконані завдання»: поточний стовпчик підсвічений, порожні мають мінімальну висоту.</summary>
public sealed class BarChartDrawable : IDrawable
{
    private static readonly Color Gold = Color.FromArgb("#C8922A");
    private static readonly Color GoldLight = Color.FromArgb("#E8B84B");
    private static readonly Color Card2 = Color.FromArgb("#1A2540");
    private static readonly Color Cream = Color.FromArgb("#F5EDD8");

    public IReadOnlyList<float> Values { get; set; } = [];
    public IReadOnlyList<string> Labels { get; set; } = [];
    public int CurrentIndex { get; set; } = -1;

    public void Draw(ICanvas canvas, RectF rect)
    {
        var n = Values.Count;
        if (n == 0 || rect.Width <= 0 || rect.Height <= 24)
        {
            return;
        }

        const float labelHeight = 16f;
        var plotHeight = rect.Height - labelHeight - 4f;

        canvas.StrokeColor = Cream.WithAlpha(0.06f);
        canvas.StrokeSize = 1f;
        for (var i = 0; i <= 3; i++)
        {
            var y = rect.Y + plotHeight * i / 3f;
            canvas.DrawLine(rect.X, y, rect.Right, y);
        }

        var max = Math.Max(1f, Values.Max());
        var slot = rect.Width / n;
        var barWidth = Math.Min(slot * 0.62f, 34f);

        for (var i = 0; i < n; i++)
        {
            var height = Math.Max(3f, Values[i] / max * (plotHeight - 4f));
            var x = rect.X + slot * i + (slot - barWidth) / 2f;
            var y = rect.Y + plotHeight - height;

            canvas.FillColor = i == CurrentIndex ? GoldLight : Values[i] > 0 ? Gold : Card2;
            canvas.FillRoundedRectangle(x, y, barWidth, height, Math.Min(5f, barWidth / 2f));

            if (i < Labels.Count && Labels[i].Length > 0)
            {
                canvas.FontColor = i == CurrentIndex ? GoldLight : Cream.WithAlpha(0.55f);
                canvas.FontSize = 9f;
                canvas.DrawString(Labels[i], rect.X + slot * i, rect.Y + plotHeight + 4f, slot, labelHeight,
                    HorizontalAlignment.Center, VerticalAlignment.Top, TextFlow.OverflowBounds);
            }
        }
    }
}