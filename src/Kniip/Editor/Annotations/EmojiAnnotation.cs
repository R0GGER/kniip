using System;
using System.Windows;
using System.Windows.Media;
using Kniip.Editor.Emoji;

namespace Kniip.Editor.Annotations;

/// <summary>Een Twemoji-stempel, gecentreerd op <see cref="Center"/>; de breedte en hoogte zijn <see cref="StyleSet.Size"/>.</summary>
public sealed class EmojiAnnotation : Annotation
{
    private static readonly Brush ShadowFar = RenderHelpers.Freeze(new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)));
    private static readonly Brush ShadowNear = RenderHelpers.Freeze(new SolidColorBrush(Color.FromArgb(55, 0, 0, 0)));

    public Point Center { get; set; }

    public bool FlipX { get; set; }

    public bool FlipY { get; set; }

    public override ToolKind Kind => ToolKind.Emoji;

    public override bool CanRotate => true;

    public override Rect Frame
    {
        get
        {
            double r = Style.Size / 2;
            return new Rect(Center.X - r, Center.Y - r, Style.Size, Style.Size);
        }
    }

    public override Rect Bounds => RotatedBounds(Frame);

    public override bool HitTest(Point point, double tolerance)
    {
        Matrix toLocal = RotationMatrix;
        toLocal.Invert();
        Rect frame = Frame;
        frame.Inflate(tolerance, tolerance);
        return frame.Contains(toLocal.Transform(point));
    }

    public override void Render(DrawingContext dc, EditorDocument document)
    {
        ImageSource image = EmojiCatalog.Get(Style.Emoji, Style.SkinTone);
        Rect bounds = Frame;
        bool flipped = FlipX || FlipY;
        double sx = FlipX ? -1 : 1, sy = FlipY ? -1 : 1;

        if (Style.Shadow)
        {
            double offset = Math.Max(2, Style.Size / 24);
            var mask = new ImageBrush(image);
            if (flipped)
            {
                mask.RelativeTransform = new ScaleTransform(sx, sy, 0.5, 0.5);
            }

            mask.Freeze();
            DrawShadowLayer(dc, mask, bounds, ShadowFar, offset * 1.6);
            DrawShadowLayer(dc, mask, bounds, ShadowNear, offset);
        }

        // De export tekent met NearestNeighbor; een geschaalde emoji moet toch vloeiend blijven.
        var group = new DrawingGroup();
        RenderOptions.SetBitmapScalingMode(group, BitmapScalingMode.HighQuality);
        using (DrawingContext gc = group.Open())
        {
            if (Angle != 0)
            {
                gc.PushTransform(new RotateTransform(Angle, Center.X, Center.Y));
            }

            if (flipped)
            {
                gc.PushTransform(new ScaleTransform(sx, sy, Center.X, Center.Y));
            }

            gc.DrawImage(image, bounds);
        }

        dc.DrawDrawing(group);
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

    /// <summary>De schaduw draait mee met de emoji, maar valt altijd rechtsonder.</summary>
    private void DrawShadowLayer(DrawingContext dc, Brush mask, Rect bounds, Brush brush, double offset)
    {
        dc.PushTransform(new TranslateTransform(offset, offset));
        dc.PushTransform(new RotateTransform(Angle, Center.X, Center.Y));
        dc.PushOpacityMask(mask);
        dc.DrawRectangle(brush, null, bounds);
        dc.Pop();
        dc.Pop();
        dc.Pop();
    }

    public override void Move(Vector delta) => Center += delta;
}
