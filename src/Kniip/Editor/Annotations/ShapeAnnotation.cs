using System.Windows;
using System.Windows.Media;

namespace Kniip.Editor.Annotations;

public sealed class ShapeAnnotation : RectAnnotation
{
    public ShapeAnnotation(bool isEllipse)
    {
        IsEllipse = isEllipse;
    }

    public bool IsEllipse { get; }

    public override ToolKind Kind => IsEllipse ? ToolKind.Ellipse : ToolKind.Rectangle;

    public override void Render(DrawingContext dc, EditorDocument document)
    {
        Rect r = Rect;
        if (r.Width < 1 && r.Height < 1)
        {
            return;
        }

        Geometry geometry = IsEllipse
            ? new EllipseGeometry(r)
            : new RectangleGeometry(r, Style.CornerRadius, Style.CornerRadius);

        RenderHelpers.DrawWithShadow(dc, geometry, RenderHelpers.BrushOrNull(Style.Fill),
            RenderHelpers.CreatePen(Style.Stroke, Style.Thickness, Style.Dash), Style.Shadow);
    }

    public override bool HitTest(Point point, double tolerance)
    {
        if (!IsEllipse)
        {
            return base.HitTest(point, tolerance);
        }

        Rect r = Rect;
        r.Inflate(tolerance + Style.Thickness / 2, tolerance + Style.Thickness / 2);
        return new EllipseGeometry(r).FillContains(point);
    }
}
