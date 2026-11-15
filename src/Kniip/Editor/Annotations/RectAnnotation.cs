using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Kniip.Editor.Annotations;

/// <summary>Basis voor annotaties die door een rechthoek worden beschreven, met 8 formaatgrepen.</summary>
public abstract class RectAnnotation : Annotation
{
    protected Rect DragOrigin { get; private set; }

    public Rect Rect { get; set; }

    public override Rect Frame => Rect;

    public override Rect Bounds => RotatedBounds(Rect);

    public override bool IsDegenerate => Rect.Width < 3 || Rect.Height < 3;

    public override bool HitTest(Point point, double tolerance)
    {
        Rect r = Rect;
        r.Inflate(tolerance, tolerance);
        return r.Contains(ToLocal(point));
    }

    public override void Move(Vector delta) => Rect = Rect.Offset(Rect, delta);

    public override IReadOnlyList<Point> GetHandles()
    {
        Rect r = Rect;
        double cx = r.Left + r.Width / 2;
        double cy = r.Top + r.Height / 2;
        var handles = new[]
        {
            r.TopLeft, new Point(cx, r.Top), r.TopRight, new Point(r.Right, cy),
            r.BottomRight, new Point(cx, r.Bottom), r.BottomLeft, new Point(r.Left, cy)
        };

        Matrix m = RotationMatrix;
        return Angle == 0 ? handles : handles.Select(p => m.Transform(p)).ToArray();
    }

    public override void BeginHandleDrag(int index) => DragOrigin = Rect;

    /// <summary>Rekent de muis terug naar het ongedraaide kader; daarna wordt het kader zo verschoven dat de tegenoverliggende greep op zijn plek blijft.</summary>
    public override void MoveHandle(int index, Point p, bool constrain)
    {
        Rect o = DragOrigin;
        var c = new Point(o.Left + o.Width / 2, o.Top + o.Height / 2);
        Matrix toLocal = Matrix.Identity;
        toLocal.RotateAt(-Angle, c.X, c.Y);
        p = toLocal.Transform(p);

        Rect r = index switch
        {
            0 => FromAnchor(o.BottomRight, p, constrain),
            1 => new Rect(new Point(o.Left, p.Y), o.BottomRight),
            2 => FromAnchor(o.BottomLeft, p, constrain),
            3 => new Rect(o.TopLeft, new Point(p.X, o.Bottom)),
            4 => FromAnchor(o.TopLeft, p, constrain),
            5 => new Rect(o.TopLeft, new Point(o.Right, p.Y)),
            6 => FromAnchor(o.TopRight, p, constrain),
            7 => new Rect(new Point(p.X, o.Top), o.BottomRight),
            _ => Rect
        };

        var rc = new Point(r.Left + r.Width / 2, r.Top + r.Height / 2);
        Matrix toWorld = Matrix.Identity;
        toWorld.RotateAt(Angle, c.X, c.Y);
        Rect = Rect.Offset(r, toWorld.Transform(rc) - rc);
    }

    /// <summary>Rechthoek tussen een vast ankerpunt en de muis; met Shift een vierkant.</summary>
    public static Rect FromAnchor(Point anchor, Point p, bool square)
    {
        if (square)
        {
            double size = System.Math.Max(System.Math.Abs(p.X - anchor.X), System.Math.Abs(p.Y - anchor.Y));
            p = new Point(anchor.X + (p.X >= anchor.X ? size : -size), anchor.Y + (p.Y >= anchor.Y ? size : -size));
        }

        return new Rect(anchor, p);
    }
}
