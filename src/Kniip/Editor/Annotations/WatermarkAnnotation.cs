using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Kniip.Watermark;

namespace Kniip.Editor.Annotations;

/// <summary>
/// Het watermerk uit de instellingen als object op het knipsel: één keer in <see cref="Rect"/>, of als patroon over het hele beeld.
/// Het heeft een eigen kopie van de instellingen, zodat aanpassen in de editor de instellingen niet raakt.
/// </summary>
public sealed class WatermarkAnnotation : Annotation
{
    private const double MinimumWidth = 8;

    public WatermarkSettings Watermark { get; set; } = new();

    /// <summary>Kader zonder draaiing, bij één watermerk.</summary>
    public Rect Rect { get; set; }

    /// <summary>Grootte van het knipsel; het patroon bedekt dit vlak en de grootte is een percentage van de breedte.</summary>
    public Size Canvas { get; set; }

    public override ToolKind Kind => ToolKind.Watermark;

    public override bool CanRotate => !Watermark.Pattern;

    public override Rect Frame => Watermark.Pattern ? new Rect(Canvas) : Rect;

    public override Rect Bounds => Watermark.Pattern ? new Rect(Canvas) : RotatedBounds(Rect);

    /// <summary>Een nieuw watermerk op de ingestelde plek; null als er niets te tekenen is.</summary>
    public static WatermarkAnnotation? Create(WatermarkSettings settings, Size canvas)
    {
        WatermarkSettings copy = settings.Clone();
        if (WatermarkRenderer.PlacedRect(canvas, copy) is not Rect rect)
        {
            return null;
        }

        return new WatermarkAnnotation { Watermark = copy, Rect = rect, Canvas = canvas };
    }

    public override void Render(DrawingContext dc, EditorDocument document)
    {
        Canvas = new Size(document.Width, document.Height);
        if (Watermark.Pattern)
        {
            WatermarkRenderer.DrawPattern(dc, Canvas, Watermark);
        }
        else
        {
            WatermarkRenderer.DrawAt(dc, Watermark, Rect, Angle);
        }
    }

    public override bool HitTest(Point point, double tolerance)
    {
        if (!Watermark.Pattern)
        {
            Rect frame = Rect;
            frame.Inflate(tolerance, tolerance);
            return frame.Contains(ToLocal(point));
        }

        foreach (Rect tile in WatermarkRenderer.PatternRects(Canvas, Watermark))
        {
            var toLocal = Matrix.Identity;
            toLocal.RotateAt(-Watermark.PatternAngle, tile.X + tile.Width / 2, tile.Y + tile.Height / 2);
            Rect inflated = tile;
            inflated.Inflate(tolerance, tolerance);
            if (inflated.Contains(toLocal.Transform(point)))
            {
                return true;
            }
        }

        return false;
    }

    public override void Move(Vector delta)
    {
        if (!Watermark.Pattern)
        {
            Rect = Rect.Offset(Rect, delta);
        }
    }

    public override IReadOnlyList<Point> GetHandles()
    {
        if (Watermark.Pattern)
        {
            return Array.Empty<Point>();
        }

        Rect r = Rect;
        Matrix m = RotationMatrix;
        return new[] { m.Transform(r.TopLeft), m.Transform(r.TopRight), m.Transform(r.BottomRight), m.Transform(r.BottomLeft) };
    }

    /// <summary>Grepen vergroten en verkleinen vanuit het midden, met dezelfde verhouding.</summary>
    public override void MoveHandle(int index, Point point, bool constrain)
    {
        Rect r = Rect;
        if (r.Width <= 0 || r.Height <= 0)
        {
            return;
        }

        var center = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        Point local = ToLocal(point);
        double aspect = r.Width / r.Height;
        double halfWidth = Math.Max(MinimumWidth / 2, Math.Max(Math.Abs(local.X - center.X), Math.Abs(local.Y - center.Y) * aspect));
        double halfHeight = halfWidth / aspect;
        Rect = new Rect(center.X - halfWidth, center.Y - halfHeight, halfWidth * 2, halfHeight * 2);
        if (Canvas.Width > 0)
        {
            Watermark.SizePercent = Rect.Width / Canvas.Width * 100;
        }
    }

    /// <summary>Na een wijziging van tekst, afbeelding of grootte: dezelfde plek (midden), de nieuwe grootte en verhouding.</summary>
    public void Refit()
    {
        if (Watermark.Pattern || WatermarkRenderer.AspectRatio(Watermark) is not double aspect || Canvas.Width <= 0)
        {
            return;
        }

        Rect r = Rect;
        var center = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        double width = Math.Max(MinimumWidth, Canvas.Width * Watermark.SizePercent / 100);
        double height = width / aspect;
        Rect = new Rect(center.X - width / 2, center.Y - height / 2, width, height);
    }

    public override Annotation Clone()
    {
        var clone = (WatermarkAnnotation)base.Clone();
        clone.Watermark = Watermark.Clone();
        return clone;
    }
}
