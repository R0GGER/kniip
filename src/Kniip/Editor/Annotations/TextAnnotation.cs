using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace Kniip.Editor.Annotations;

/// <summary>
/// Tekst los of in een kader, ballon, ovaal of wolk. Ballon, ovaal en wolk hebben een staart naar <see cref="TailTip"/>.
/// Zonder <see cref="BoxWidth"/> past de vorm zich aan de tekst aan; met een vaste breedte loopt de tekst door naar de volgende regel.
/// </summary>
public sealed class TextAnnotation : Annotation
{
    private const int TailHandle = 8;

    private Rect _dragOrigin;
    private ShapeKey _cachedKey;
    private Geometry? _cachedShape;

    public Point Position { get; set; }

    public string Text { get; set; } = string.Empty;

    /// <summary>Vaste breedte van de vorm (zonder staart); null = passend om de tekst.</summary>
    public double? BoxWidth { get; set; }

    /// <summary>Minimale hoogte van de vorm; past de tekst er niet in, dan wordt hij hoger.</summary>
    public double? BoxHeight { get; set; }

    /// <summary>Punt waar de staart naar wijst, in afbeeldingspixels; null = standaard linksonder de vorm.</summary>
    public Point? TailTip { get; set; }

    public override ToolKind Kind => ToolKind.Text;

    public double Padding => Math.Round(Math.Max(4, Style.FontSize * 0.4)) + Math.Max(0, Style.Thickness) / 2;

    public bool HasTail => Style.TextShape is not (TextShape.Frame or TextShape.None);

    /// <summary>Straal van de bolletjes langs de rand van een wolk.</summary>
    private double BumpRadius => Math.Max(6, Style.FontSize * 0.7);

    private readonly record struct Layout(Rect Box, Rect Area, FormattedText Text, Point Origin);

    private readonly record struct ShapeKey(Rect Box, Point Tip, TextShape Shape, double Corner, double Bump, double Thickness, int Bubbles);

    /// <summary>Van de breedte of hoogte van het tekstvlak naar die van de vorm.</summary>
    private double ToBox(double area) => Style.TextShape switch
    {
        TextShape.Oval => area * Math.Sqrt(2),
        TextShape.Cloud => area * Math.Sqrt(2) + 2 * BumpRadius,
        _ => area
    };

    private double ToArea(double box) => Style.TextShape switch
    {
        TextShape.Oval => box / Math.Sqrt(2),
        TextShape.Cloud => Math.Max(0, box - 2 * BumpRadius) / Math.Sqrt(2),
        _ => box
    };

    public double MinBoxWidth => ToBox(Style.FontSize * 2 + 2 * Padding);

    private FormattedText Format(string text, Color color)
    {
        FormattedText ft = RenderHelpers.FormatText(text, Style.FontSize, color, Style.Bold);
        ft.Trimming = TextTrimming.None;
        ft.TextAlignment = Style.TextAlign;
        return ft;
    }

    private Layout GetLayout()
    {
        FormattedText ft = Format(Text.Length == 0 ? " " : Text, Style.Foreground);
        double pad = Padding;
        double boxWidth;
        if (BoxWidth is double fixedWidth)
        {
            boxWidth = Math.Max(fixedWidth, MinBoxWidth);
            ft.MaxTextWidth = Math.Max(1, ToArea(boxWidth) - 2 * pad + 0.01);
        }
        else
        {
            boxWidth = ToBox(ft.WidthIncludingTrailingWhitespace + 2 * pad);
            // Zonder breedte lijnt WPF centreren en rechts uit rond de oorsprong in plaats van binnen het vlak.
            ft.MaxTextWidth = Math.Max(1, ft.WidthIncludingTrailingWhitespace + 0.01);
        }

        double boxHeight = Math.Max(BoxHeight ?? 0, ToBox(ft.Height + 2 * pad));
        var box = new Rect(Position, new Size(boxWidth, boxHeight));
        double areaWidth = ToArea(boxWidth);
        double areaHeight = ToArea(boxHeight);
        var area = new Rect(box.X + (boxWidth - areaWidth) / 2, box.Y + (boxHeight - areaHeight) / 2, areaWidth, areaHeight);
        var origin = new Point(area.X + pad, area.Y + (area.Height - ft.Height) / 2);
        return new Layout(box, area, ft, origin);
    }

    /// <summary>De vorm zonder staart; hier zitten de formaatgrepen.</summary>
    public Rect Box => GetLayout().Box;

    /// <summary>Linksboven van de eerste regel tekst.</summary>
    public Point TextOrigin => GetLayout().Origin;

    /// <summary>Breedte waarop de tekst afbreekt, of null als de vorm met de tekst meegroeit.</summary>
    public double? WrapWidth => BoxWidth == null ? null : GetLayout().Text.MaxTextWidth;

    public Point DefaultTailTip(Rect box) =>
        new(box.Left - Style.FontSize, box.Bottom + Style.FontSize * 2);

    private Point Tip(Rect box) => TailTip ?? DefaultTailTip(box);

    public override Rect Bounds
    {
        get
        {
            Rect box = GetLayout().Box;
            Rect r = ShapeFor(box).Bounds;
            r.Union(box);
            double half = Math.Max(0, Style.Thickness) / 2;
            r.Inflate(half, half);
            return r;
        }
    }

    public override bool IsDegenerate => string.IsNullOrWhiteSpace(Text);

    public override bool HitTest(Point point, double tolerance)
    {
        Geometry shape = ShapeFor(GetLayout().Box);
        if (shape.FillContains(point))
        {
            return true;
        }

        var pen = new Pen(Brushes.Black, 2 * tolerance + Math.Max(0, Style.Thickness));
        return shape.StrokeContains(pen, point);
    }

    public override void Render(DrawingContext dc, EditorDocument document)
    {
        if (string.IsNullOrEmpty(Text))
        {
            return;
        }

        Layout layout = GetLayout();
        bool hasShape = RenderShape(dc, layout.Box);
        if (!hasShape && Style.Shadow)
        {
            FormattedText shadow = Format(Text, Color.FromArgb(90, 0, 0, 0));
            shadow.MaxTextWidth = layout.Text.MaxTextWidth;
            dc.DrawText(shadow, new Point(layout.Origin.X + 1.5, layout.Origin.Y + 1.5));
        }

        dc.DrawText(layout.Text, layout.Origin);
    }

    /// <summary>Alleen de vorm, zonder tekst; wordt getekend terwijl de tekst in een tekstvak wordt bewerkt.</summary>
    public void RenderOutline(DrawingContext dc) => RenderShape(dc, GetLayout().Box);

    /// <summary>True als er een rand of vulling is getekend.</summary>
    private bool RenderShape(DrawingContext dc, Rect box)
    {
        if (Style.TextShape == TextShape.None)
        {
            return false;
        }

        Brush? fill = RenderHelpers.BrushOrNull(Style.Fill);
        Pen? pen = RenderHelpers.CreatePen(Style.Stroke, Style.Thickness, Style.Dash);
        if (fill == null && pen == null)
        {
            return false;
        }

        RenderHelpers.DrawWithShadow(dc, ShapeFor(box), fill, pen, Style.Shadow);
        return true;
    }

    // ---------------------------------------------------------------- Vorm

    private Geometry ShapeFor(Rect box)
    {
        var key = new ShapeKey(box, HasTail ? Tip(box) : default, Style.TextShape, Style.CornerRadius, BumpRadius, Style.Thickness, Style.CloudBubbles);
        if (_cachedShape == null || key != _cachedKey)
        {
            _cachedShape = BuildShape(box, key.Tip);
            _cachedKey = key;
        }

        return _cachedShape;
    }

    private Geometry BuildShape(Rect box, Point tip)
    {
        Geometry shape = Style.TextShape switch
        {
            TextShape.Balloon => WithTail(new RectangleGeometry(box, Style.CornerRadius, Style.CornerRadius), BalloonTail(box, tip)),
            TextShape.Oval => WithTail(new EllipseGeometry(box), OvalTail(box, tip)),
            TextShape.Cloud => Cloud(box, tip),
            _ => new RectangleGeometry(box, Style.CornerRadius, Style.CornerRadius)
        };

        shape.Freeze();
        return shape;
    }

    private static Geometry WithTail(Geometry body, Geometry? tail) =>
        tail == null ? body : Geometry.Combine(body, tail, GeometryCombineMode.Union, null);

    private static Geometry Triangle(Point a, Point b, Point tip)
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(a, true, true);
            ctx.LineTo(tip, true, true);
            ctx.LineTo(b, true, true);
        }

        return geometry;
    }

    /// <summary>Spitse staart vanaf de zijde die naar de punt kijkt.</summary>
    private Geometry? BalloonTail(Rect box, Point tip)
    {
        if (box.Contains(tip))
        {
            return null;
        }

        Point center = new(box.X + box.Width / 2, box.Y + box.Height / 2);
        Vector d = tip - center;
        bool horizontal = Math.Abs(d.X) / Math.Max(1, box.Width) > Math.Abs(d.Y) / Math.Max(1, box.Height);
        double corner = Math.Min(Style.CornerRadius, Math.Min(box.Width, box.Height) / 2);
        double inset = Math.Max(1, Style.Thickness) + 1;

        double side = horizontal ? box.Height : box.Width;
        double half = Math.Max(1, Math.Min(side * 0.125, (side - 2 * corner) / 2));
        double low = (horizontal ? box.Top : box.Left) + corner + half;
        double high = (horizontal ? box.Bottom : box.Right) - corner - half;
        double middle = horizontal ? center.Y : center.X;
        double toward = horizontal ? tip.Y : tip.X;
        double c = low > high ? middle : Math.Clamp((middle + toward) / 2, low, high);

        if (horizontal)
        {
            double x = d.X < 0 ? box.Left + inset : box.Right - inset;
            return Triangle(new Point(x, c - half), new Point(x, c + half), tip);
        }

        double y = d.Y < 0 ? box.Top + inset : box.Bottom - inset;
        return Triangle(new Point(c - half, y), new Point(c + half, y), tip);
    }

    private static Point OnEllipse(Point center, double rx, double ry, double angle) =>
        new(center.X + rx * Math.Cos(angle), center.Y + ry * Math.Sin(angle));

    private static bool InsideEllipse(Point center, double rx, double ry, Point p)
    {
        if (rx <= 0 || ry <= 0)
        {
            return false;
        }

        double x = (p.X - center.X) / rx;
        double y = (p.Y - center.Y) / ry;
        return x * x + y * y <= 1;
    }

    /// <summary>Afstand van een punt tot de ellips, gemeten langs de lijn vanuit het midden; negatief als het punt erbinnen ligt.</summary>
    private static double OutsideDistance(Point center, double rx, double ry, Point p)
    {
        Vector v = p - center;
        double scale = Math.Sqrt(Math.Pow(v.X / rx, 2) + Math.Pow(v.Y / ry, 2));
        return scale < 1e-9 ? -Math.Min(rx, ry) : v.Length * (1 - 1 / scale);
    }

    /// <summary>Hoek op de ellips (in de genormaliseerde ruimte) in de richting van de punt.</summary>
    private static double AngleToward(Point center, double rx, double ry, Point tip) =>
        Math.Atan2((tip.Y - center.Y) / Math.Max(1, ry), (tip.X - center.X) / Math.Max(1, rx));

    private Geometry? OvalTail(Rect box, Point tip)
    {
        Point center = new(box.X + box.Width / 2, box.Y + box.Height / 2);
        double rx = box.Width / 2, ry = box.Height / 2;
        if (InsideEllipse(center, rx, ry, tip))
        {
            return null;
        }

        double angle = AngleToward(center, rx, ry, tip);
        const double spread = 0.24;
        const double inward = 0.94;
        return Triangle(
            OnEllipse(center, rx * inward, ry * inward, angle - spread),
            OnEllipse(center, rx * inward, ry * inward, angle + spread),
            tip);
    }

    /// <summary>Bolletjes langs een ellips, met als staart steeds kleinere rondjes naar de punt; automatisch meer rondjes naarmate de punt verder weg ligt.</summary>
    private Geometry Cloud(Rect box, Point tip)
    {
        double r = BumpRadius;
        Point center = new(box.X + box.Width / 2, box.Y + box.Height / 2);
        double rx = Math.Max(1, box.Width / 2 - r);
        double ry = Math.Max(1, box.Height / 2 - r);

        double h = Math.Pow((rx - ry) / (rx + ry), 2);
        double perimeter = Math.PI * (rx + ry) * (1 + 3 * h / (10 + Math.Sqrt(4 - 3 * h)));
        int count = Math.Max(6, (int)Math.Round(perimeter / (r * 1.75)));

        Geometry cloud = new EllipseGeometry(center, rx, ry);
        for (int i = 0; i < count; i++)
        {
            Point p = OnEllipse(center, rx, ry, 2 * Math.PI * i / count);
            cloud = Geometry.Combine(cloud, new EllipseGeometry(p, r, r), GeometryCombineMode.Union, null);
        }

        if (!HasTail || InsideEllipse(center, rx + r, ry + r, tip))
        {
            return cloud;
        }

        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        group.Children.Add(cloud);

        // Vanaf de punt terug naar de wolk, gelijk verdeeld; rondjes die de wolk zouden raken vallen weg.
        Vector back = center - tip;
        back.Normalize();

        double gap = Math.Max(2, r * 0.35);
        double smallest = Math.Max(2, r * 0.32);
        double largest = Math.Max(smallest, r * 0.72);
        double pitch = 2 * largest + gap;
        double available = Math.Max(0, OutsideDistance(center, rx + r, ry + r, tip) - gap - largest);
        int bubbles = Style.CloudBubbles > 0
            ? Style.CloudBubbles
            : Math.Clamp((int)(available / pitch) + 1, 1, 40);
        double spacing = bubbles > 1 ? available / (bubbles - 1) : 0;
        double shrink = bubbles > 1 ? Math.Min(1, spacing / pitch) : 1;

        for (int i = 0; i < bubbles; i++)
        {
            double t = bubbles > 1 ? (double)i / (bubbles - 1) : 0.5;
            double radius = Math.Max(1, (smallest + (largest - smallest) * t) * shrink);
            Point c = tip + back * (spacing * i);
            if (OutsideDistance(center, rx + r, ry + r, c) < radius * 0.8)
            {
                break;
            }

            group.Children.Add(new EllipseGeometry(c, radius, radius));
        }

        return group;
    }

    // ---------------------------------------------------------------- Grepen

    public override void Move(Vector delta)
    {
        Position += delta;
        if (TailTip is Point tip)
        {
            TailTip = tip + delta;
        }
    }

    /// <summary>Volgorde: 8 formaatgrepen zoals bij <see cref="RectAnnotation"/>, daarna de staartpunt.</summary>
    public override IReadOnlyList<Point> GetHandles()
    {
        Rect r = GetLayout().Box;
        double cx = r.Left + r.Width / 2;
        double cy = r.Top + r.Height / 2;
        var handles = new List<Point>
        {
            r.TopLeft, new Point(cx, r.Top), r.TopRight, new Point(r.Right, cy),
            r.BottomRight, new Point(cx, r.Bottom), r.BottomLeft, new Point(r.Left, cy)
        };

        if (HasTail)
        {
            handles.Add(Tip(r));
        }

        return handles;
    }

    public override void BeginHandleDrag(int index)
    {
        _dragOrigin = GetLayout().Box;
        TailTip ??= DefaultTailTip(_dragOrigin);
    }

    public override void MoveHandle(int index, Point p, bool constrain)
    {
        if (index == TailHandle)
        {
            TailTip = p;
            return;
        }

        Rect o = _dragOrigin;
        Rect r = index switch
        {
            0 => RectAnnotation.FromAnchor(o.BottomRight, p, constrain),
            1 => new Rect(new Point(o.Left, p.Y), o.BottomRight),
            2 => RectAnnotation.FromAnchor(o.BottomLeft, p, constrain),
            3 => new Rect(o.TopLeft, new Point(p.X, o.Bottom)),
            4 => RectAnnotation.FromAnchor(o.TopLeft, p, constrain),
            5 => new Rect(o.TopLeft, new Point(o.Right, p.Y)),
            6 => RectAnnotation.FromAnchor(o.TopRight, p, constrain),
            7 => new Rect(new Point(p.X, o.Top), o.BottomRight),
            _ => o
        };

        double minWidth = MinBoxWidth;
        if (r.Width < minWidth)
        {
            bool leftEdge = index is 0 or 6 or 7 && p.X < o.Right;
            r = new Rect(leftEdge ? r.Right - minWidth : r.Left, r.Top, minWidth, r.Height);
        }

        Position = r.TopLeft;
        BoxWidth = r.Width;
        BoxHeight = r.Height;
    }

    /// <summary>Dubbelklik op een formaatgreep: terug naar een vorm die met de tekst meegroeit.</summary>
    public override bool RemoveHandle(int index)
    {
        if (index >= TailHandle || (BoxWidth == null && BoxHeight == null))
        {
            return false;
        }

        BoxWidth = null;
        BoxHeight = null;
        return true;
    }

    public override Annotation Clone()
    {
        var clone = (TextAnnotation)base.Clone();
        clone._cachedShape = null;
        return clone;
    }
}
