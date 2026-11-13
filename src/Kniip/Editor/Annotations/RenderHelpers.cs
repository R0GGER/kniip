using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Kniip.Editor.Annotations;

internal static class RenderHelpers
{
    private static readonly FontFamily UiFont = new("Segoe UI");
    private static readonly Brush ShadowFar = Freeze(new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)));
    private static readonly Brush ShadowNear = Freeze(new SolidColorBrush(Color.FromArgb(55, 0, 0, 0)));

    public static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    public static Brush? BrushOrNull(Color color) =>
        color.A == 0 ? null : Freeze(new SolidColorBrush(color));

    public static Pen? CreatePen(Color color, double thickness, LineDash dash, PenLineCap cap = PenLineCap.Round)
    {
        if (thickness <= 0 || color.A == 0)
        {
            return null;
        }

        var pen = new Pen(new SolidColorBrush(color), thickness)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = cap,
            EndLineCap = cap
        };

        switch (dash)
        {
            case LineDash.Dash:
                pen.DashStyle = new DashStyle(new[] { 3.0, 2.0 }, 0);
                pen.DashCap = PenLineCap.Flat;
                break;
            case LineDash.Dot:
                pen.DashStyle = new DashStyle(new[] { 0.0, 2.0 }, 0);
                pen.DashCap = PenLineCap.Round;
                break;
        }

        return Freeze(pen);
    }

    /// <summary>Tekent een geometrie met een zachte schaduw rechtsonder.</summary>
    public static void DrawWithShadow(DrawingContext dc, Geometry geometry, Brush? fill, Pen? pen, bool shadow)
    {
        if (shadow)
        {
            double offset = Math.Max(2, (pen?.Thickness ?? 2) * 0.6);
            DrawShadowLayer(dc, geometry, fill != null, pen, ShadowFar, offset * 1.6);
            DrawShadowLayer(dc, geometry, fill != null, pen, ShadowNear, offset);
        }

        dc.DrawGeometry(fill, pen, geometry);
    }

    private static void DrawShadowLayer(DrawingContext dc, Geometry geometry, bool filled, Pen? pen, Brush brush, double offset)
    {
        Pen? shadowPen = null;
        if (pen != null)
        {
            shadowPen = new Pen(brush, pen.Thickness)
            {
                LineJoin = pen.LineJoin,
                StartLineCap = pen.StartLineCap,
                EndLineCap = pen.EndLineCap,
                DashStyle = pen.DashStyle,
                DashCap = pen.DashCap
            };
            shadowPen.Freeze();
        }

        dc.PushTransform(new TranslateTransform(offset, offset));
        dc.DrawGeometry(filled ? brush : null, shadowPen, geometry);
        dc.Pop();
    }

    public static FormattedText FormatText(string text, double fontSize, Color color, bool bold) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(UiFont, FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal),
            fontSize, Freeze(new SolidColorBrush(color)), 1.0);

    public static double DistanceToSegment(Point p, Point a, Point b)
    {
        Vector ab = b - a;
        double lengthSquared = ab.LengthSquared;
        if (lengthSquared < 1e-9)
        {
            return (p - a).Length;
        }

        double t = Math.Clamp(Vector.Multiply(p - a, ab) / lengthSquared, 0, 1);
        return (p - (a + t * ab)).Length;
    }

    public static Point SnapAngle(Point origin, Point target, double stepDegrees = 45)
    {
        Vector v = target - origin;
        double length = v.Length;
        if (length < 1e-6)
        {
            return target;
        }

        double angle = Math.Atan2(v.Y, v.X);
        double step = stepDegrees * Math.PI / 180;
        angle = Math.Round(angle / step) * step;
        return origin + new Vector(Math.Cos(angle) * length, Math.Sin(angle) * length);
    }
}
