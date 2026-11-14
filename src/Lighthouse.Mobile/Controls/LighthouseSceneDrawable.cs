using Microsoft.Maui.Graphics;

namespace Lighthouse.Mobile.Controls;

/// <summary>Нічна сцена з маяком для екрана привітання (малюється кодом за макетом 390×844, масштабується під екран).</summary>
public sealed class LighthouseSceneDrawable : IDrawable
{
    private static readonly Color Navy = Color.FromArgb("#0A0E1A");
    private static readonly Color Deep = Color.FromArgb("#0D1526");
    private static readonly Color Gold = Color.FromArgb("#C8922A");
    private static readonly Color GoldLight = Color.FromArgb("#E8B84B");

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var w = dirtyRect.Width;
        var h = dirtyRect.Height;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        var u = Math.Min(w / 390f, h / 844f);
        var cx = dirtyRect.X + w / 2f;

        // Геометрія: море знизу (під кнопками), скеля, маяк на скелі.
        var seaTop = h - 190f * u;
        var cliffTop = seaTop - 52f * u;
        var baseBottom = cliffTop + 8f * u;
        var baseTop = baseBottom - 9f * u;
        var towerTop = baseTop - 72f * u;
        var galleryTop = towerTop - 6f * u;
        var lanternTop = galleryTop - 26f * u;
        var lanternCy = lanternTop + 13f * u;

        DrawSky(canvas, dirtyRect);
        DrawStars(canvas, dirtyRect, u);

        Beam(canvas, cx, lanternCy, 22f * u, 120f * u, 340f * u, 0.16f);
        Beam(canvas, cx, lanternCy, 10f * u, 55f * u, 340f * u, 0.5f);
        for (var i = 0; i < 6; i++)
        {
            canvas.FillColor = GoldLight.WithAlpha(0.03f + i * 0.025f);
            canvas.FillCircle(cx, lanternCy, (62f - i * 9f) * u);
        }

        DrawSea(canvas, dirtyRect, seaTop, u);
        DrawCliff(canvas, cx, cliffTop, seaTop, u);
        DrawLighthouse(canvas, cx, baseTop, baseBottom, towerTop, galleryTop, lanternTop, u);

        canvas.FillColor = GoldLight.WithAlpha(0.3f);
        canvas.FillEllipse(cx - 45f * u, seaTop + 14f * u, 90f * u, 20f * u);
    }

    private static void DrawSky(ICanvas canvas, RectF rect)
    {
        var sky = new LinearGradientPaint
        {
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 1),
            GradientStops =
            [
                new PaintGradientStop(0f, Navy),
                new PaintGradientStop(0.55f, Deep),
                new PaintGradientStop(1f, Color.FromArgb("#1E4470"))
            ]
        };
        canvas.SetFillPaint(sky, rect);
        canvas.FillRectangle(rect);
    }

    private static void DrawStars(ICanvas canvas, RectF rect, float u)
    {
        var random = new Random(7); // фіксоване зерно: однакове небо щоразу
        for (var i = 0; i < 110; i++)
        {
            var x = rect.X + (float)random.NextDouble() * rect.Width;
            var y = rect.Y + (float)random.NextDouble() * rect.Height * 0.62f;
            var radius = (0.5f + (float)random.NextDouble() * 1.3f) * Math.Max(u, 0.8f);
            canvas.FillColor = Colors.White.WithAlpha(0.2f + (float)random.NextDouble() * 0.6f);
            canvas.FillCircle(x, y, radius);
        }
    }

    /// <summary>Промінь від ліхтаря вгору: трапеція з градієнтом прозорості.</summary>
    private static void Beam(ICanvas canvas, float cx, float y, float halfBottom, float halfTop, float length, float alpha)
    {
        var path = new PathF();
        path.MoveTo(cx - halfBottom, y);
        path.LineTo(cx + halfBottom, y);
        path.LineTo(cx + halfTop, y - length);
        path.LineTo(cx - halfTop, y - length);
        path.Close();

        var paint = new LinearGradientPaint
        {
            StartPoint = new Point(0.5, 1),
            EndPoint = new Point(0.5, 0),
            GradientStops =
            [
                new PaintGradientStop(0f, GoldLight.WithAlpha(alpha)),
                new PaintGradientStop(1f, GoldLight.WithAlpha(0f))
            ]
        };
        canvas.SetFillPaint(paint, new RectF(cx - halfTop, y - length, halfTop * 2f, length));
        canvas.FillPath(path);
    }

    private static void DrawSea(ICanvas canvas, RectF rect, float seaTop, float u)
    {
        var sea = new RectF(rect.X, seaTop, rect.Width, rect.Bottom - seaTop);
        canvas.SetFillPaint(new LinearGradientPaint
        {
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 1),
            GradientStops =
            [
                new PaintGradientStop(0f, Color.FromArgb("#0F2645")),
                new PaintGradientStop(1f, Color.FromArgb("#07142A"))
            ]
        }, sea);
        canvas.FillRectangle(sea);

        var shimmer = new RectF(rect.X, seaTop, rect.Width, 2f * u);
        canvas.SetFillPaint(new LinearGradientPaint
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5),
            GradientStops =
            [
                new PaintGradientStop(0f, Gold.WithAlpha(0f)),
                new PaintGradientStop(0.5f, GoldLight.WithAlpha(0.6f)),
                new PaintGradientStop(1f, Gold.WithAlpha(0f))
            ]
        }, shimmer);
        canvas.FillRectangle(shimmer);
    }

    private static void DrawCliff(ICanvas canvas, float cx, float cliffTop, float seaTop, float u)
    {
        var path = new PathF();
        path.MoveTo(cx - 66f * u, seaTop + 10f * u);
        path.LineTo(cx - 52f * u, cliffTop + 34f * u);
        path.LineTo(cx - 24f * u, cliffTop + 10f * u);
        path.LineTo(cx + 10f * u, cliffTop);
        path.LineTo(cx + 42f * u, cliffTop + 8f * u);
        path.LineTo(cx + 62f * u, cliffTop + 34f * u);
        path.LineTo(cx + 68f * u, seaTop + 10f * u);
        path.Close();

        canvas.SetFillPaint(new LinearGradientPaint
        {
            StartPoint = new Point(0.3, 0),
            EndPoint = new Point(0.7, 1),
            GradientStops =
            [
                new PaintGradientStop(0f, Color.FromArgb("#1E2F4A")),
                new PaintGradientStop(1f, Color.FromArgb("#0F1D32"))
            ]
        }, new RectF(cx - 66f * u, cliffTop, 134f * u, seaTop + 10f * u - cliffTop));
        canvas.FillPath(path);
    }

    private static void DrawLighthouse(
        ICanvas canvas, float cx, float baseTop, float baseBottom, float towerTop, float galleryTop, float lanternTop, float u)
    {
        // Кам'яна основа.
        canvas.FillColor = Color.FromArgb("#7E6E58");
        canvas.FillRoundedRectangle(cx - 20f * u, baseTop, 40f * u, baseBottom - baseTop, 3f * u);

        // Вежа: трапеція з вертикальним градієнтом і горизонтальними смугами.
        const float topHalf = 11f;
        const float bottomHalf = 17f;
        var tower = new PathF();
        tower.MoveTo(cx - topHalf * u, towerTop);
        tower.LineTo(cx + topHalf * u, towerTop);
        tower.LineTo(cx + bottomHalf * u, baseTop);
        tower.LineTo(cx - bottomHalf * u, baseTop);
        tower.Close();

        canvas.SetFillPaint(new LinearGradientPaint
        {
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 1),
            GradientStops =
            [
                new PaintGradientStop(0f, Color.FromArgb("#ECE4D4")),
                new PaintGradientStop(0.5f, Color.FromArgb("#CDC3AE")),
                new PaintGradientStop(1f, Color.FromArgb("#A89E8C"))
            ]
        }, new RectF(cx - bottomHalf * u, towerTop, bottomHalf * 2f * u, baseTop - towerTop));
        canvas.FillPath(tower);

        canvas.StrokeColor = Colors.Black.WithAlpha(0.08f);
        canvas.StrokeSize = 1f;
        for (var y = towerTop + 10f * u; y < baseTop - 2f; y += 10f * u)
        {
            var half = (topHalf + (bottomHalf - topHalf) * (y - towerTop) / (baseTop - towerTop)) * u;
            canvas.DrawLine(cx - half, y, cx + half, y);
        }

        // Галерея, ліхтар і дах.
        canvas.FillColor = Gold;
        canvas.FillRoundedRectangle(cx - 19f * u, galleryTop, 38f * u, 6f * u, 2f * u);

        var lantern = new RectF(cx - 14f * u, lanternTop, 28f * u, 26f * u);
        canvas.SetFillPaint(new LinearGradientPaint
        {
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 1),
            GradientStops =
            [
                new PaintGradientStop(0f, Color.FromArgb("#F5D742")),
                new PaintGradientStop(1f, Gold)
            ]
        }, lantern);
        canvas.FillRoundedRectangle(lantern, 5f * u);

        canvas.FillColor = Color.FromArgb("#FFF6CF");
        canvas.FillRoundedRectangle(lantern.X + 3f * u, lantern.Y + 3f * u, lantern.Width - 6f * u, lantern.Height - 6f * u, 3f * u);

        var roof = new PathF();
        roof.MoveTo(cx - 17f * u, lanternTop);
        roof.LineTo(cx, lanternTop - 16f * u);
        roof.LineTo(cx + 17f * u, lanternTop);
        roof.Close();
        canvas.FillColor = Gold;
        canvas.FillPath(roof);
    }
}