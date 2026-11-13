using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Kniip.Editor.Annotations;

/// <summary>Een vorm uit het vormengereedschap (driehoek, ster, hart, ...), geschaald naar zijn kader.</summary>
public sealed class FigureAnnotation : RectAnnotation
{
    public bool FlipX { get; set; }

    public bool FlipY { get; set; }

    public override ToolKind Kind => ToolKind.Shape;

    public override bool CanRotate => true;

    public override Rect Frame => Rect;

    public override Rect Bounds => RotatedBounds(Rect);

    private bool IsOpen => ShapeGeometries.IsOpen(Style.ShapeKind);

    /// <summary>De vorm in beeldcoördinaten; via de geometrietransformatie, zodat de lijndikte niet meeschaalt.</summary>
    private Geometry BuildGeometry()
    {
        Geometry source = ShapeGeometries.For(Style.ShapeKind);
        Rect b = source.Bounds;
        Rect r = Rect;
        double cx = r.Left + r.Width / 2, cy = r.Top + r.Height / 2;
        var m = Matrix.Identity;
        m.Translate(-(b.Left + b.Width / 2), -(b.Top + b.Height / 2));
        m.Scale(r.Width / b.Width * (FlipX ? -1 : 1), r.Height / b.Height * (FlipY ? -1 : 1));
        m.Translate(cx, cy);
        m.RotateAt(Angle, cx, cy);

        Geometry geometry = source.Clone();
        geometry.Transform = new MatrixTransform(m);
        geometry.Freeze();
        return geometry;
    }

    public override void Render(DrawingContext dc, EditorDocument document)
    {
        Rect r = Rect;
        if (r.Width < 1 && r.Height < 1)
        {
            return;
        }

        Brush? fill = IsOpen ? null : RenderHelpers.BrushOrNull(Style.Fill);
        RenderHelpers.DrawWithShadow(dc, BuildGeometry(), fill,
            RenderHelpers.CreatePen(Style.Stroke, Style.Thickness, Style.Dash), Style.Shadow);
    }

    public override bool HitTest(Point point, double tolerance)
    {
        Geometry geometry = BuildGeometry();
        if (!IsOpen && geometry.FillContains(point))
        {
            return true;
        }

        var pen = new Pen(Brushes.Black, Style.Thickness + 2 * tolerance);
        return geometry.StrokeContains(pen, point);
    }

    public override IReadOnlyList<Point> GetHandles()
    {
        Matrix m = RotationMatrix;
        return base.GetHandles().Select(p => m.Transform(p)).ToArray();
    }

    /// <summary>Rekent de muis terug naar het ongedraaide kader; daarna wordt het kader zo verschoven dat de tegenoverliggende greep op zijn plek blijft.</summary>
    public override void MoveHandle(int index, Point p, bool constrain)
    {
        Rect o = DragOrigin;
        var c = new Point(o.Left + o.Width / 2, o.Top + o.Height / 2);
        Matrix toLocal = Matrix.Identity;
        toLocal.RotateAt(-Angle, c.X, c.Y);
        base.MoveHandle(index, toLocal.Transform(p), constrain);

        Rect r = Rect;
        var rc = new Point(r.Left + r.Width / 2, r.Top + r.Height / 2);
        Matrix toWorld = Matrix.Identity;
        toWorld.RotateAt(Angle, c.X, c.Y);
        Rect = Rect.Offset(r, toWorld.Transform(rc) - rc);
    }

    public override bool CanFlip => true;

    public override void Flip(bool horizontal)
    {
        if (horizontal)
        {
            FlipX = !FlipX;
        }
        else
        {
            FlipY = !FlipY;
        }

        Angle = NormalizeAngle(-Angle);
    }
}
