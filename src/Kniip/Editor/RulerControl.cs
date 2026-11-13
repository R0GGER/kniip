using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Kniip.Editor;

/// <summary>
/// Liniaal langs het canvas met een schaalverdeling in pixels van de afbeelding.
/// Sleep vanuit de liniaal om een hulplijn te maken; de editor regelt het slepen zelf.
/// </summary>
public sealed class RulerControl : FrameworkElement
{
    private static readonly double[] Steps = { 1, 2, 5, 10, 20, 25, 50, 100, 200, 250, 500, 1000, 2000, 2500, 5000, 10000 };
    private static readonly Typeface LabelFace = new("Segoe UI");

    public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(
        nameof(Background), typeof(Brush), typeof(RulerControl), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground), typeof(Brush), typeof(RulerControl), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BorderBrushProperty = DependencyProperty.Register(
        nameof(BorderBrush), typeof(Brush), typeof(RulerControl), new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ImageBackgroundProperty = DependencyProperty.Register(
        nameof(ImageBackground), typeof(Brush), typeof(RulerControl), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private double _origin;
    private double _scale = 1;
    private double _length;
    private double? _marker;
    private IReadOnlyList<double> _guides = Array.Empty<double>();

    public Orientation Orientation { get; set; } = Orientation.Horizontal;

    public Brush? Background
    {
        get => (Brush?)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public Brush BorderBrush
    {
        get => (Brush)GetValue(BorderBrushProperty);
        set => SetValue(BorderBrushProperty, value);
    }

    /// <summary>Achtergrond van het stuk liniaal naast de afbeelding.</summary>
    public Brush? ImageBackground
    {
        get => (Brush?)GetValue(ImageBackgroundProperty);
        set => SetValue(ImageBackgroundProperty, value);
    }

    /// <summary>Plaats van afbeeldingspixel 0 in de liniaal (DIP), de schaal (DIP per pixel) en de lengte van de afbeelding in pixels.</summary>
    public void SetView(double origin, double scale, double length)
    {
        if (Math.Abs(origin - _origin) < 0.01 && Math.Abs(scale - _scale) < 1e-6 && Math.Abs(length - _length) < 0.01)
        {
            return;
        }

        _origin = origin;
        _scale = scale;
        _length = length;
        InvalidateVisual();
    }

    /// <summary>Positie van de muis in afbeeldingspixels, of null als de muis niet boven het canvas is.</summary>
    public void SetMarker(double? value)
    {
        if (_marker == value)
        {
            return;
        }

        _marker = value;
        InvalidateVisual();
    }

    public void SetGuides(IReadOnlyList<double> guides)
    {
        _guides = guides;
        InvalidateVisual();
    }

    /// <summary>Afbeeldingscoördinaat bij een positie (DIP) in de liniaal.</summary>
    public double ToImage(double position) => (position - _origin) / _scale;

    private bool Horizontal => Orientation == Orientation.Horizontal;

    protected override void OnRender(DrawingContext dc)
    {
        double along = Horizontal ? ActualWidth : ActualHeight;
        double across = Horizontal ? ActualHeight : ActualWidth;
        if (along <= 0 || across <= 0 || _scale <= 0)
        {
            return;
        }

        // Ook transparant tekenen, zodat de hele liniaal muisklikken ontvangt.
        dc.DrawRectangle(Background ?? Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (ImageBackground != null && _length > 0)
        {
            double start = Math.Max(0, _origin), end = Math.Min(along, _origin + _length * _scale);
            if (end > start)
            {
                dc.DrawRectangle(ImageBackground, null, Span(start, end - start, 0, across));
            }
        }

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var tickPen = new Pen(Foreground, 1 / dpi);
        tickPen.Freeze();

        double major = Steps[^1];
        foreach (double step in Steps)
        {
            if (step * _scale >= 50)
            {
                major = step;
                break;
            }
        }

        double minor = major;
        foreach (int divisions in new[] { 10, 5, 2 })
        {
            if (major % divisions == 0 && major / divisions * _scale >= 5)
            {
                minor = major / divisions;
                break;
            }
        }

        double first = Math.Floor(ToImage(0) / minor) * minor;
        double last = ToImage(along);
        for (double value = first; value <= last; value += minor)
        {
            double pos = Snap(_origin + value * _scale, dpi);
            bool isMajor = Math.Abs(value % major) < 1e-6;
            bool isHalf = !isMajor && Math.Abs(value % (major / 2)) < 1e-6;
            double tick = isMajor ? across : isHalf ? across * 0.45 : across * 0.25;
            dc.DrawLine(tickPen, Point(pos, across - tick), Point(pos, across));

            if (isMajor)
            {
                DrawLabel(dc, value.ToString(CultureInfo.CurrentCulture), pos, dpi);
            }
        }

        var guidePen = new Pen(new SolidColorBrush(AnnotationSurface.GuideColor), 1 / dpi);
        guidePen.Freeze();
        foreach (double guide in _guides)
        {
            double pos = Snap(_origin + guide * _scale, dpi);
            dc.DrawLine(guidePen, Point(pos, 0), Point(pos, across));
        }

        if (_marker is double marker)
        {
            var markerPen = new Pen((Brush)FindResource("AccentBrush"), 1 / dpi);
            markerPen.Freeze();
            double pos = Snap(_origin + marker * _scale, dpi);
            dc.DrawLine(markerPen, Point(pos, 0), Point(pos, across));
        }

        var border = new Pen(BorderBrush, 1 / dpi);
        border.Freeze();
        double edge = across - 0.5 / dpi;
        dc.DrawLine(border, Point(0, edge), Point(along, edge));
    }

    private void DrawLabel(DrawingContext dc, string text, double pos, double dpi)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, LabelFace, 9, Foreground, dpi);
        if (Horizontal)
        {
            dc.DrawText(ft, new Point(pos + 3, 1));
            return;
        }

        dc.PushTransform(new RotateTransform(-90, 1, pos - 3));
        dc.DrawText(ft, new Point(1, pos - 3));
        dc.Pop();
    }

    private static double Snap(double value, double dpi) => (Math.Round(value * dpi) + 0.5) / dpi;

    private Point Point(double along, double across) => Horizontal ? new Point(along, across) : new Point(across, along);

    private Rect Span(double along, double alongLength, double across, double acrossLength) =>
        Horizontal ? new Rect(along, across, alongLength, acrossLength) : new Rect(across, along, acrossLength, alongLength);
}
