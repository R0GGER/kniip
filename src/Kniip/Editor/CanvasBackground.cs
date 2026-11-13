using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Kniip.Editor;

public enum BackgroundKind { None, Color, Gradient, Image }

public enum GradientDirection { Linear, Radial }

/// <summary>
/// Wat er onder het knipsel ligt; alleen zichtbaar waar het knipsel transparant is.
/// De kleuren en afbeelding blijven bewaard bij het wisselen van soort.
/// </summary>
/// <param name="Angle">Richting van een lineair verloop in graden, met de klok mee vanaf boven: 90 = naar rechts, 180 = naar onder.</param>
public sealed record CanvasBackground(
    BackgroundKind Kind,
    Color Color,
    Color Color2,
    GradientDirection Direction,
    double Angle,
    BitmapSource? Image)
{
    public static readonly CanvasBackground Default = new(
        BackgroundKind.None, Colors.White, Color.FromRgb(0x00, 0x78, 0xD4), GradientDirection.Linear, 180, null);

    public bool IsVisible => Kind switch
    {
        BackgroundKind.None => false,
        BackgroundKind.Image => Image != null,
        _ => true
    };

    public void Render(DrawingContext dc, int width, int height)
    {
        var area = new Rect(0, 0, width, height);
        switch (Kind)
        {
            case BackgroundKind.Color:
                dc.DrawRectangle(new SolidColorBrush(Color), null, area);
                break;
            case BackgroundKind.Gradient:
                dc.DrawRectangle(CreateGradient(), null, area);
                break;
            case BackgroundKind.Image when Image != null:
                // Eigen schaalmodus: de export tekent verder met NearestNeighbor.
                var group = new DrawingGroup { ClipGeometry = new RectangleGeometry(area) };
                RenderOptions.SetBitmapScalingMode(group, BitmapScalingMode.HighQuality);
                group.Children.Add(new ImageDrawing(Image, Cover(Image.PixelWidth, Image.PixelHeight, area)));
                dc.DrawDrawing(group);
                break;
        }
    }

    private Brush CreateGradient()
    {
        if (Direction == GradientDirection.Radial)
        {
            return new RadialGradientBrush(Color, Color2);
        }

        // In de eenheidsruimte van het vlak, zodat 45° van hoek naar hoek loopt; de halve lengte reikt tot in de hoeken.
        double radians = Angle * Math.PI / 180;
        var direction = new Vector(Math.Sin(radians), -Math.Cos(radians));
        double half = (Math.Abs(direction.X) + Math.Abs(direction.Y)) / 2;
        var center = new Point(0.5, 0.5);
        return new LinearGradientBrush(Color, Color2, center - direction * half, center + direction * half);
    }

    /// <summary>Afbeelding zo schalen dat ze het hele vlak vult; wat uitsteekt valt weg.</summary>
    private static Rect Cover(double imageWidth, double imageHeight, Rect area)
    {
        double scale = Math.Max(area.Width / imageWidth, area.Height / imageHeight);
        double w = imageWidth * scale, h = imageHeight * scale;
        return new Rect(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
    }
}
