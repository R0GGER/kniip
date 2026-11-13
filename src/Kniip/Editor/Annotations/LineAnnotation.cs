using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace Kniip.Editor.Annotations;

/// <summary>
/// Lijn of pijl. Met buigpunten wordt het een vloeiende kromme die door alle punten loopt.
/// </summary>
public sealed class LineAnnotation : Annotation
{
    private const int SamplesPerSegment = 16;

    private int _dragBend = -1;
    private int _pendingGhost = -1;

    public LineAnnotation(bool isArrow)
    {
        IsArrow = isArrow;
    }

    public bool IsArrow { get; }

    public Point Start { get; set; }

    public Point End { get; set; }

    /// <summary>Tussenpunten waar de kromme doorheen loopt, in volgorde van begin naar eind.</summary>
    public List<Point> Bends { get; set; } = new();

    public override ToolKind Kind => IsArrow ? ToolKind.Arrow : ToolKind.Line;

    public override Rect Bounds
    {
        get
        {
            List<Point> points = Flatten();
            var r = new Rect(points[0], points[0]);
            foreach (Point p in points)
            {
                r.Union(p);
            }

            double pad = Style.Thickness / 2 + (IsArrow ? HeadLength / 2 : 0);
            r.Inflate(pad, pad);
            return r;
        }
    }

    public override bool IsDegenerate => Bends.Count == 0 && (End - Start).Length < 4;

    private double HeadLength => Math.Max(12, Style.Thickness * 4);

    private readonly record struct Segment(Point A, Point C1, Point C2, Point B)
    {
        public Point At(double t)
        {
            double u = 1 - t;
            double a = u * u * u, b = 3 * u * u * t, c = 3 * u * t * t, d = t * t * t;
            return new Point(
                a * A.X + b * C1.X + c * C2.X + d * B.X,
                a * A.Y + b * C1.Y + c * C2.Y + d * B.Y);
        }
    }

    private List<Point> ThroughPoints()
    {
        var points = new List<Point>(Bends.Count + 2) { Start };
        points.AddRange(Bends);
        points.Add(End);
        return points;
    }

    /// <summary>Catmull-Rom door alle punten, omgezet naar kubische Bezier-segmenten.</summary>
    private List<Segment> Segments()
    {
        List<Point> p = ThroughPoints();
        int last = p.Count - 1;
        var segments = new List<Segment>(last);
        for (int i = 0; i < last; i++)
        {
            Point prev = p[Math.Max(i - 1, 0)];
            Point next = p[Math.Min(i + 2, last)];
            segments.Add(new Segment(p[i], p[i] + (p[i + 1] - prev) / 6, p[i + 1] - (next - p[i]) / 6, p[i + 1]));
        }

        return segments;
    }

    private List<Point> Flatten()
    {
        if (Bends.Count == 0)
        {
            return new List<Point> { Start, End };
        }

        var points = new List<Point> { Start };
        foreach (Segment s in Segments())
        {
            for (int k = 1; k <= SamplesPerSegment; k++)
            {
                points.Add(s.At(k / (double)SamplesPerSegment));
            }
        }

        return points;
    }

    private static double PathLength(List<Point> points)
    {
        double length = 0;
        for (int i = 1; i < points.Count; i++)
        {
            length += (points[i] - points[i - 1]).Length;
        }

        return length;
    }

    private Geometry BuildGeometry(List<Segment> segments)
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(segments[0].A, false, false);
            foreach (Segment s in segments)
            {
                if (Bends.Count == 0)
                {
                    ctx.LineTo(s.B, true, true);
                }
                else
                {
                    ctx.BezierTo(s.C1, s.C2, s.B, true, true);
                }
            }
        }

        geometry.Freeze();
        return geometry;
    }

    public override void Render(DrawingContext dc, EditorDocument document)
    {
        double length = PathLength(Flatten());
        if (length < 1)
        {
            return;
        }

        Pen? pen = RenderHelpers.CreatePen(Style.Stroke, Style.Thickness, Style.Dash);
        if (pen == null)
        {
            return;
        }

        List<Segment> segments = Segments();

        if (IsArrow)
        {
            Segment lastSegment = segments[^1];
            Vector v = lastSegment.B - lastSegment.C2;
            if (v.Length < 1e-6)
            {
                v = lastSegment.B - lastSegment.A;
            }

            v.Normalize();
            double headLength = Math.Min(HeadLength, length);
            double halfWidth = headLength * 0.45;
            Point basePoint = End - v * headLength;
            var normal = new Vector(-v.Y, v.X);

            Point lineEnd = basePoint + v * Math.Min(2, headLength / 4);
            Vector shift = lineEnd - End;
            segments[^1] = lastSegment with { C2 = lastSegment.C2 + shift, B = lineEnd };

            var head = new StreamGeometry();
            using (StreamGeometryContext ctx = head.Open())
            {
                ctx.BeginFigure(End, true, true);
                ctx.LineTo(basePoint + normal * halfWidth, true, true);
                ctx.LineTo(basePoint - normal * halfWidth, true, true);
            }

            head.Freeze();
            Brush? headFill = RenderHelpers.BrushOrNull(Style.Stroke);

            // Lijn en punt apart tekenen zodat de punt gevuld en niet gestippeld wordt.
            RenderHelpers.DrawWithShadow(dc, BuildGeometry(segments), null, pen, Style.Shadow);
            var headPen = RenderHelpers.CreatePen(Style.Stroke, Math.Max(1, Style.Thickness / 3), LineDash.Solid);
            RenderHelpers.DrawWithShadow(dc, head, headFill, headPen, Style.Shadow);
            return;
        }

        RenderHelpers.DrawWithShadow(dc, BuildGeometry(segments), null, pen, Style.Shadow);
    }

    public override bool HitTest(Point point, double tolerance)
    {
        double limit = tolerance + Style.Thickness / 2 + (IsArrow ? 4 : 0);
        List<Point> points = Flatten();
        for (int i = 1; i < points.Count; i++)
        {
            if (RenderHelpers.DistanceToSegment(point, points[i - 1], points[i]) <= limit)
            {
                return true;
            }
        }

        return false;
    }

    public override void Move(Vector delta)
    {
        Start += delta;
        End += delta;
        for (int i = 0; i < Bends.Count; i++)
        {
            Bends[i] += delta;
        }
    }

    /// <summary>Volgorde: begin, eind, buigpunten, en daarna één spook-greep midden op elk segment.</summary>
    public override IReadOnlyList<Point> GetHandles()
    {
        var handles = new List<Point> { Start, End };
        handles.AddRange(Bends);
        foreach (Segment s in Segments())
        {
            handles.Add(s.At(0.5));
        }

        return handles;
    }

    public override bool IsGhostHandle(int index) => index >= 2 + Bends.Count;

    public override void BeginHandleDrag(int index)
    {
        _pendingGhost = IsGhostHandle(index) ? index - 2 - Bends.Count : -1;
        _dragBend = index >= 2 && _pendingGhost < 0 ? index - 2 : -1;
    }

    public override void MoveHandle(int index, Point point, bool constrain)
    {
        // Het buigpunt pas bij de eerste beweging invoegen, zodat een losse klik de lijn niet verandert.
        if (_pendingGhost >= 0)
        {
            Bends.Insert(_pendingGhost, point);
            _dragBend = _pendingGhost;
            _pendingGhost = -1;
            return;
        }

        if (_dragBend >= 0)
        {
            Bends[_dragBend] = point;
        }
        else if (index == 0)
        {
            Start = constrain ? RenderHelpers.SnapAngle(Bends.Count > 0 ? Bends[0] : End, point) : point;
        }
        else if (index == 1)
        {
            End = constrain ? RenderHelpers.SnapAngle(Bends.Count > 0 ? Bends[^1] : Start, point) : point;
        }
    }

    public override bool RemoveHandle(int index)
    {
        int bend = index - 2;
        if (bend < 0 || bend >= Bends.Count)
        {
            return false;
        }

        Bends.RemoveAt(bend);
        return true;
    }

    public override bool CanFlip => true;

    public override void Flip(bool horizontal)
    {
        Start = Mirror(Start, horizontal);
        End = Mirror(End, horizontal);
        for (int i = 0; i < Bends.Count; i++)
        {
            Bends[i] = Mirror(Bends[i], horizontal);
        }
    }

    public override Annotation Clone()
    {
        var clone = (LineAnnotation)base.Clone();
        clone.Bends = new List<Point>(Bends);
        return clone;
    }
}
