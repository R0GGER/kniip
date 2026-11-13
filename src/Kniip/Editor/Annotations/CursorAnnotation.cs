using System.Windows;
using System.Windows.Media;

namespace Kniip.Editor.Annotations;

/// <summary>Een muiscursor-stempel. <see cref="Position"/> is de "hotspot" (punt van de pijl of vinger).</summary>
public sealed class CursorAnnotation : Annotation
{
    private const double DesignSize = 24;

    public Point Position { get; set; }

    /// <summary>Gespiegeld rond de hotspot.</summary>
    public bool FlipX { get; set; }

    public bool FlipY { get; set; }

    public override ToolKind Kind => ToolKind.Cursor;

    private double Scale => Style.Size / DesignSize;

    public override Rect Bounds
    {
        get
        {
            Rect r = CursorGeometries.For(Style.CursorKind).Bounds;
            double x = FlipX ? -r.Right : r.X;
            double y = FlipY ? -r.Bottom : r.Y;
            double s = Scale;
            return new Rect(Position.X + x * s, Position.Y + y * s, r.Width * s, r.Height * s);
        }
    }

    public override void Render(DrawingContext dc, EditorDocument document)
    {
        dc.PushTransform(new TranslateTransform(Position.X, Position.Y));
        dc.PushTransform(new ScaleTransform(Scale, Scale));

        // De geometrie spiegelen in plaats van de hele tekening, zodat de schaduw rechtsonder blijft vallen.
        CursorGeometries.Parts parts = CursorGeometries.PartsFor(Style.CursorKind);
        RenderHelpers.DrawWithShadow(dc, Mirrored(parts.Body), Brushes.White, CursorGeometries.OutlinePen, Style.Shadow);
        if (parts.Details != null)
        {
            dc.DrawGeometry(null, CursorGeometries.DetailPen, Mirrored(parts.Details));
        }

        if (parts.Rays != null)
        {
            dc.DrawGeometry(null, CursorGeometries.RayPen, Mirrored(parts.Rays));
        }

        dc.Pop();
        dc.Pop();
    }

    private Geometry Mirrored(Geometry geometry)
    {
        if (!FlipX && !FlipY)
        {
            return geometry;
        }

        PathGeometry copy = PathGeometry.CreateFromGeometry(geometry);
        Matrix matrix = copy.Transform?.Value ?? Matrix.Identity;
        matrix.Scale(FlipX ? -1 : 1, FlipY ? -1 : 1);
        copy.Transform = new MatrixTransform(matrix);
        copy.Freeze();
        return copy;
    }

    public override void Move(Vector delta) => Position += delta;

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
    }
}
