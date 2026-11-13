using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Kniip.Editor.Annotations;

namespace Kniip.Editor;

/// <summary>
/// Tekent het knipsel met alle annotaties, plus de selectie en het bijsnijkader.
/// Het element is precies zo groot als de afbeelding in pixels; zoomen gebeurt met een LayoutTransform erboven.
/// </summary>
public sealed class AnnotationSurface : FrameworkElement
{
    private static readonly Brush AccentBrush = RenderHelpers.Freeze(new SolidColorBrush(Palette.Accent));
    private static readonly Brush GhostHandleBrush = RenderHelpers.Freeze(new SolidColorBrush(Color.FromArgb(170, 255, 255, 255)));
    private static readonly Brush CropShadeBrush = RenderHelpers.Freeze(new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)));
    private static readonly Brush CheckerLight = RenderHelpers.Freeze(new SolidColorBrush(Colors.White));
    private static readonly Brush CheckerDark = RenderHelpers.Freeze(new SolidColorBrush(Color.FromRgb(0xEF, 0xEF, 0xEF)));

    public static readonly Color GuideColor = Color.FromRgb(0, 180, 255);
    private static readonly Brush GuideBrush = RenderHelpers.Freeze(new SolidColorBrush(GuideColor));
    private static readonly Brush SnapBrush = RenderHelpers.Freeze(new SolidColorBrush(Color.FromRgb(255, 45, 160)));

    public EditorDocument? Document { get; set; }

    public Annotation? Selected { get; set; }

    public Rect? CropRect { get; set; }

    /// <summary>Verticale hulplijnen (X in afbeeldingspixels); worden niet geëxporteerd.</summary>
    public IReadOnlyList<double> GuidesX { get; set; } = System.Array.Empty<double>();

    /// <summary>Horizontale hulplijnen (Y in afbeeldingspixels).</summary>
    public IReadOnlyList<double> GuidesY { get; set; } = System.Array.Empty<double>();

    public bool ShowGuides { get; set; }

    /// <summary>Lijn waaraan het object tijdens het slepen vastklikt, om die op te laten lichten.</summary>
    public double? SnapX { get; set; }

    public double? SnapY { get; set; }

    /// <summary>Omtrek van de gum onder de muis, in afbeeldingspixels.</summary>
    public (Point Center, double Radius)? BrushPreview { get; set; }

    /// <summary>Schaal van de weergave (DIP per afbeeldingspixel), om grepen altijd even groot te tonen.</summary>
    public double ViewScale { get; set; } = 1;

    public double HandleSize => 9 / ViewScale;

    public AnnotationSurface()
    {
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
    }

    protected override Size MeasureOverride(Size availableSize) =>
        Document == null ? new Size(0, 0) : new Size(Document.Width, Document.Height);

    protected override void OnRender(DrawingContext dc)
    {
        if (Document == null)
        {
            return;
        }

        if (Document.HasTransparency)
        {
            dc.DrawRectangle(CreateChecker(), null, new Rect(0, 0, Document.Width, Document.Height));
        }

        DocumentRenderer.Render(dc, Document);

        if (Selected != null && !Selected.IsHidden && Document.Items.Contains(Selected))
        {
            DrawSelection(dc, Selected);
        }

        if (CropRect is Rect crop)
        {
            var full = new RectangleGeometry(new Rect(0, 0, Document.Width, Document.Height));
            var shade = Geometry.Combine(full, new RectangleGeometry(crop), GeometryCombineMode.Exclude, null);
            dc.DrawGeometry(CropShadeBrush, null, shade);
            dc.DrawRectangle(null, CreateDashedPen(Brushes.White), crop);
            dc.DrawRectangle(null, CreateDashedPen(AccentBrush, 4), crop);
        }

        if (ShowGuides)
        {
            DrawGuides(dc, Document);
        }

        if (BrushPreview is { } brush)
        {
            double radius = System.Math.Max(brush.Radius, 2 / ViewScale);
            dc.DrawEllipse(null, new Pen(Brushes.Black, 2.5 / ViewScale), brush.Center, radius, radius);
            dc.DrawEllipse(null, new Pen(Brushes.White, 1 / ViewScale), brush.Center, radius, radius);
        }
    }

    private void DrawGuides(DrawingContext dc, EditorDocument document)
    {
        var guidePen = new Pen(GuideBrush, 1 / ViewScale);
        var snapPen = new Pen(SnapBrush, 1 / ViewScale);
        foreach (double x in GuidesX)
        {
            dc.DrawLine(guidePen, new Point(x, 0), new Point(x, document.Height));
        }

        foreach (double y in GuidesY)
        {
            dc.DrawLine(guidePen, new Point(0, y), new Point(document.Width, y));
        }

        if (SnapX is double sx)
        {
            dc.DrawLine(snapPen, new Point(sx, 0), new Point(sx, document.Height));
        }

        if (SnapY is double sy)
        {
            dc.DrawLine(snapPen, new Point(0, sy), new Point(document.Width, sy));
        }
    }

    /// <summary>Index van de hulplijn onder het punt; <paramref name="vertical"/> is true voor een verticale lijn (X).</summary>
    public int HitGuide(Point p, out bool vertical)
    {
        double tolerance = 4 / ViewScale;
        vertical = true;
        for (int i = GuidesX.Count - 1; i >= 0; i--)
        {
            if (System.Math.Abs(GuidesX[i] - p.X) <= tolerance)
            {
                return i;
            }
        }

        vertical = false;
        for (int i = GuidesY.Count - 1; i >= 0; i--)
        {
            if (System.Math.Abs(GuidesY[i] - p.Y) <= tolerance)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Plek van de draaigreep: een vaste schermafstand boven het (gedraaide) kader.</summary>
    public Point? RotateHandle(Annotation item)
    {
        if (!item.CanRotate)
        {
            return null;
        }

        Rect f = item.Frame;
        var local = new Point(f.X + f.Width / 2, f.Top - (3 + 22) / ViewScale);
        return item.RotationMatrix.Transform(local);
    }

    public bool HitRotateHandle(Annotation item, Point p) =>
        RotateHandle(item) is Point h && (h - p).Length <= HandleSize / 2 + 3 / ViewScale;

    private void DrawSelection(DrawingContext dc, Annotation item)
    {
        double pad = 3 / ViewScale;
        double size = HandleSize;
        var pen = new Pen(AccentBrush, 1.2 / ViewScale);

        if (item.CanRotate)
        {
            Rect frame = item.Frame;
            frame.Inflate(pad, pad);
            dc.PushTransform(new MatrixTransform(item.RotationMatrix));
            dc.DrawRectangle(null, CreateDashedPen(AccentBrush), frame);
            dc.DrawLine(pen, new Point(frame.X + frame.Width / 2, frame.Top), new Point(frame.X + frame.Width / 2, frame.Top - 22 / ViewScale));
            dc.Pop();
            if (RotateHandle(item) is Point rotate)
            {
                dc.DrawEllipse(Brushes.White, pen, rotate, size / 2, size / 2);
                dc.DrawEllipse(AccentBrush, null, rotate, size / 5, size / 5);
            }
        }
        else
        {
            Rect bounds = item.Bounds;
            bounds.Inflate(pad, pad);
            dc.DrawRectangle(null, CreateDashedPen(AccentBrush), bounds);
        }

        IReadOnlyList<Point> handles = item.GetHandles();
        if (handles.Count == 0)
        {
            return;
        }

        for (int i = 0; i < handles.Count; i++)
        {
            Point h = handles[i];
            if (item.IsGhostHandle(i))
            {
                double radius = size * 0.35;
                dc.DrawEllipse(GhostHandleBrush, pen, h, radius, radius);
                continue;
            }

            dc.DrawRoundedRectangle(Brushes.White, pen, new Rect(h.X - size / 2, h.Y - size / 2, size, size),
                1.5 / ViewScale, 1.5 / ViewScale);
        }
    }

    /// <summary>Blokjespatroon voor transparantie; de blokjes blijven op elk zoomniveau even groot op het scherm.</summary>
    private Brush CreateChecker()
    {
        double cell = 8 / ViewScale;
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(CheckerLight, null, new RectangleGeometry(new Rect(0, 0, 2, 2))));
        drawing.Children.Add(new GeometryDrawing(CheckerDark, null, Geometry.Parse("M0,0 H1 V1 H0 Z M1,1 H2 V2 H1 Z")));
        return new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, cell * 2, cell * 2),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.Fill
        };
    }

    private Pen CreateDashedPen(Brush brush, double offset = 0) => new(brush, 1 / ViewScale)
    {
        DashStyle = new DashStyle(new[] { 4.0, 4.0 }, offset)
    };

    public int HitHandle(Annotation item, Point p)
    {
        IReadOnlyList<Point> handles = item.GetHandles();
        double tolerance = HandleSize / 2 + 2 / ViewScale;
        int ghost = -1;
        for (int i = handles.Count - 1; i >= 0; i--)
        {
            if (System.Math.Abs(handles[i].X - p.X) <= tolerance && System.Math.Abs(handles[i].Y - p.Y) <= tolerance)
            {
                if (!item.IsGhostHandle(i))
                {
                    return i;
                }

                ghost = i;
            }
        }

        return ghost;
    }
}
