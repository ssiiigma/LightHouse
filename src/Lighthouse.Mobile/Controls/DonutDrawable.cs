using Microsoft.Maui.Graphics;

namespace Lighthouse.Mobile.Controls;

/// <summary>Кільцева діаграма розподілу за категоріями; сектори будуються ламаною (без залежності від напрямку кутів API).</summary>
public sealed class DonutDrawable : IDrawable
{
    private static readonly Color Track = Color.FromArgb("#1A2540");
    private static readonly Color Cream = Color.FromArgb("#F5EDD8");

    public IReadOnlyList<(float Fraction, Color Color)> Segments { get; set; } = [];
    public string CenterText { get; set; } = string.Empty;

    public void Draw(ICanvas canvas, RectF rect)
    {
        var size = Math.Min(rect.Width, rect.Height);
        if (size <= 0)
        {
            return;
        }

        const float thickness = 14f;
        var radius = (size - thickness) / 2f - 1f;
        var cx = rect.Center.X;
        var cy = rect.Center.Y;

        canvas.StrokeSize = thickness;
        canvas.StrokeLineCap = LineCap.Butt;
        canvas.StrokeLineJoin = LineJoin.Round;
        canvas.StrokeColor = Track;
        canvas.DrawCircle(cx, cy, radius);

        var start = 0f;
        var gap = Segments.Count > 1 ? 0.008f : 0f;
        foreach (var (fraction, color) in Segments)
        {
            if (fraction <= 0f)
            {
                continue;
            }

            var from = start + gap / 2f;
            var sweep = Math.Max(0.002f, fraction - gap);
            var steps = Math.Max(2, (int)(sweep * 360f / 2f));

            var path = new PathF();
            for (var i = 0; i <= steps; i++)
            {
                var angle = -MathF.PI / 2f + 2f * MathF.PI * (from + sweep * i / steps);
                var x = cx + radius * MathF.Cos(angle);
                var y = cy + radius * MathF.Sin(angle);
                if (i == 0)
                {
                    path.MoveTo(x, y);
                }
                else
                {
                    path.LineTo(x, y);
                }
            }

            canvas.StrokeColor = color;
            canvas.DrawPath(path);
            start += fraction;
        }

        if (CenterText.Length > 0)
        {
            canvas.FontColor = Cream;
            canvas.FontSize = 18f;
            canvas.DrawString(CenterText, cx - radius, cy - 12f, radius * 2f, 24f,
                HorizontalAlignment.Center, VerticalAlignment.Center);
        }
    }
}