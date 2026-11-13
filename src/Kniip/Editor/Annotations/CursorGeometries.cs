using System.Windows;
using System.Windows.Media;

namespace Kniip.Editor.Annotations;

/// <summary>Vectorcursors in een 24-eenheden ontwerpraster, met de hotspot op (0,0).</summary>
internal static class CursorGeometries
{
    public sealed record Parts(Geometry Body, Geometry? Details, Geometry? Rays);

    public static readonly Pen OutlinePen = RenderHelpers.Freeze(new Pen(Brushes.Black, 1.2) { LineJoin = PenLineJoin.Round });
    public static readonly Pen DetailPen = RenderHelpers.Freeze(new Pen(Brushes.Black, 0.9) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
    public static readonly Pen RayPen = RenderHelpers.Freeze(new Pen(new SolidColorBrush(Palette.Red), 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });

    private static readonly Geometry ArrowBody = RenderHelpers.Freeze(
        Geometry.Parse("M0,0 L0,19.5 L4.7,15.2 L7.8,22.2 L11,20.8 L7.9,14 L14,14 Z"));

    private static readonly Parts ArrowParts = new(ArrowBody, null, null);

    private static readonly Parts HandParts = CreateHand();

    private static readonly Parts ClickParts = new(ArrowBody, null, RenderHelpers.Freeze(
        Geometry.Parse("M-3,-3 L-6.5,-6.5 M0,-4.5 L0,-9 M3,-3 L6.5,-6.5 M-4.5,0 L-9,0")));

    public static Parts PartsFor(CursorKind kind) => kind switch
    {
        CursorKind.Hand => HandParts,
        CursorKind.Click => ClickParts,
        _ => ArrowParts
    };

    public static Geometry For(CursorKind kind)
    {
        Parts parts = PartsFor(kind);
        if (parts.Rays == null)
        {
            return parts.Body;
        }

        var group = new GeometryGroup();
        group.Children.Add(parts.Body);
        group.Children.Add(parts.Rays);
        group.Freeze();
        return group;
    }

    private static Parts CreateHand()
    {
        // Wijsvinger omhoog; hotspot op de vingertop.
        Geometry hand = new RectangleGeometry(new Rect(5, 0, 4, 13), 2, 2);
        hand = Union(hand, new RectangleGeometry(new Rect(9, 6.5, 3.6, 8), 1.8, 1.8));
        hand = Union(hand, new RectangleGeometry(new Rect(12.6, 7.5, 3.4, 8), 1.7, 1.7));
        hand = Union(hand, new RectangleGeometry(new Rect(16, 9, 3.2, 7), 1.6, 1.6));
        hand = Union(hand, new RectangleGeometry(new Rect(5, 11, 14.2, 11.5), 4, 4));
        var thumb = new RectangleGeometry(new Rect(0.5, 11.5, 8, 3.8), 1.9, 1.9)
        {
            Transform = new RotateTransform(45, 4.5, 13.4)
        };

        Geometry body = Geometry.Combine(hand, thumb, GeometryCombineMode.Union, new TranslateTransform(-7, 0));
        body.Freeze();

        Geometry details = Geometry.Parse("M2,7.5 L2,12 M5.6,8.5 L5.6,12 M9,10 L9,12");
        details.Freeze();
        return new Parts(body, details, null);
    }

    private static Geometry Union(Geometry a, Geometry b) =>
        Geometry.Combine(a, b, GeometryCombineMode.Union, null);
}
