using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Kniip.Editor.Annotations;

/// <summary>Markeerstift: half-transparant, als rechte horizontale streep of uit de vrije hand.</summary>
public sealed class HighlightAnnotation : Annotation
{
    private const byte HighlightAlpha = 110;

    public List<Point> Points { get; set; } = new();

    public bool Straight { get; set; }

    public override ToolKind Kind => ToolKind.Highlight;

    public override Rect Bounds
    {
        get
        {
            if (Points.Count == 0)
            {
                return Rect.Empty;
            }

            double minX = Points.Min(p => p.X), maxX = Points.Max(p => p.X);
            double minY = Points.Min(p => p.Y), maxY = Points.Max(p => p.Y);
            var r = new Rect(new Point(minX, minY), new Point(maxX, maxY));
            r.Inflate(Style.Thickness / 2, Style.Thickness / 2);
            return r;
        }
    }

    public override bool IsDegenerate
    {
        get
        {
            if (Points.Count < 2)
            {
                return true;
            }

            Rect r = Bounds;
            return r.Width - Style.Thickness < 3 && r.Height - Style.Thickness < 3;
        }
    }

    public override void Render(DrawingContext dc, EditorDocument document)
    {
        if (Points.Count < 2)
        {
            return;
        }

        Color c = Style.Stroke;
        var color = Color.FromArgb(HighlightAlpha, c.R, c.G, c.B);
        Pen? pen = RenderHelpers.CreatePen(color, Style.Thickness, LineDash.Solid,
            Straight ? PenLineCap.Square : PenLineCap.Round);
        if (pen == null)
        {
            return;
        }

        // Eén geometrie, zodat overlappende delen niet dubbel zo donker worden.
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(Points[0], false, false);
            ctx.PolyLineTo(Points.Skip(1).ToList(), true, true);
        }

        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    public override bool HitTest(Point point, double tolerance)
    {
        double limit = tolerance + Style.Thickness / 2;
        for (int i = 1; i < Points.Count; i++)
        {
            if (RenderHelpers.DistanceToSegment(point, Points[i - 1], Points[i]) <= limit)
            {
                return true;
            }
        }

        return false;
    }

    public override void Move(Vector delta)
    {
        for (int i = 0; i < Points.Count; i++)
        {
            Points[i] += delta;
        }
    }

    public override IReadOnlyList<Point> GetHandles() =>
        Straight && Points.Count == 2 ? new[] { Points[0], Points[1] } : Array.Empty<Point>();

    public override void MoveHandle(int index, Point point, bool constrain)
    {
        if (!Straight || Points.Count != 2)
        {
            return;
        }

        Points[index] = new Point(point.X, Points[1 - index].Y);
    }

    public override bool CanFlip => !Straight;

    public override void Flip(bool horizontal)
    {
        for (int i = 0; i < Points.Count; i++)
        {
            Points[i] = Mirror(Points[i], horizontal);
        }
    }

    public override Annotation Clone()
    {
        var clone = (HighlightAnnotation)base.Clone();
        clone.Points = new List<Point>(Points);
        return clone;
    }
}
