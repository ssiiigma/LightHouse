using Microsoft.Maui.Graphics;

namespace Lighthouse.Mobile.Controls;

/// <summary>Теплова карта: стовпець = тиждень, рядок = день тижня (пн зверху); рівні 0–4, −1 = майбутній день.</summary>
public sealed class HeatmapDrawable : IDrawable
{
    private static readonly Color Gold = Color.FromArgb("#C8922A");
    private static readonly Color Card2 = Color.FromArgb("#1A2540");
    private static readonly Color Cream = Color.FromArgb("#F5EDD8");

    public int[] Levels { get; set; } = [];
    public string[] RowLabels { get; set; } = [];

    public void Draw(ICanvas canvas, RectF rect)
    {
        var weeks = Levels.Length / 7;
        if (weeks == 0 || rect.Width <= 0)
        {
            return;
        }

        const float labelWidth = 26f;
        const float gap = 3f;
        var cellByWidth = (rect.Width - labelWidth - gap * (weeks - 1)) / weeks;
        var cellByHeight = (rect.Height - gap * 6f) / 7f;
        var cell = Math.Max(4f, Math.Min(cellByWidth, cellByHeight));

        canvas.FontColor = Cream.WithAlpha(0.55f);
        canvas.FontSize = 9f;
        for (var row = 0; row < 7; row++)
        {
            var y = rect.Y + row * (cell + gap);
            if (row < RowLabels.Length && RowLabels[row].Length > 0)
            {
                canvas.DrawString(RowLabels[row], rect.X, y, labelWidth - 2f, cell,
                    HorizontalAlignment.Left, VerticalAlignment.Center);
            }

            for (var week = 0; week < weeks; week++)
            {
                var level = Levels[week * 7 + row];
                if (level < 0)
                {
                    continue;
                }

                canvas.FillColor = level switch
                {
                    0 => Card2,
                    1 => Gold.WithAlpha(0.25f),
                    2 => Gold.WithAlpha(0.5f),
                    3 => Gold.WithAlpha(0.75f),
                    _ => Gold
                };
                canvas.FillRoundedRectangle(rect.X + labelWidth + week * (cell + gap), y, cell, cell, 3f);
            }
        }
    }
}