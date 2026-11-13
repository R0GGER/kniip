using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rectangle = System.Drawing.Rectangle;
using Screen = System.Windows.Forms.Screen;

namespace Kniip.Recording;

/// <summary>
/// Overlay over één beeldscherm om het opnamegebied te kiezen. Er staat er één per scherm;
/// een gebied ligt daardoor altijd binnen één scherm.
/// </summary>
public partial class RegionSelectorWindow : Window
{
    private const double DragThreshold = 4;
    private const int MinimumSize = 16;

    private readonly Screen _screen;
    private readonly IReadOnlyList<Rectangle> _windows;
    private readonly BitmapSource? _frozen;
    private Point? _dragStart;
    private bool _dragging;
    private Rectangle? _hover;

    /// <summary>Gekozen gebied, of null bij annuleren.</summary>
    internal event Action<RecordingRegion?>? Finished;

    /// <param name="frozen">Beeld van het scherm voor het vergrootglas; null betekent geen vergrootglas.</param>
    internal RegionSelectorWindow(Screen screen, IReadOnlyList<Rectangle> windows, BitmapSource? frozen = null)
    {
        InitializeComponent();
        _screen = screen;
        _windows = windows;
        _frozen = frozen;
        SourceInitialized += (_, _) => PhysicalWindow.Place(this, screen.Bounds);
        Loaded += (_, _) => ShowSelection(null);
        MouseLeave += (_, _) =>
        {
            Magnifier.Hide();
            if (!_dragging)
            {
                _hover = null;
                ShowSelection(null);
            }
        };
    }

    public bool ContainsCursor(System.Drawing.Point cursor) => _screen.Bounds.Contains(cursor);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Point position = e.GetPosition(this);
        if (_frozen != null)
        {
            System.Drawing.Point physical = ToPhysical(position);
            var relative = new System.Drawing.Point(physical.X - _screen.Bounds.X, physical.Y - _screen.Bounds.Y);
            Magnifier.Show(_frozen, relative, physical, position, new Size(ActualWidth, ActualHeight));
        }

        if (_dragStart is Point start && e.LeftButton == MouseButtonState.Pressed)
        {
            if (!_dragging && (position - start).Length >= DragThreshold)
            {
                _dragging = true;
            }

            if (_dragging)
            {
                ShowSelection(ToPhysical(new Rect(start, position)));
            }

            return;
        }

        _hover = FindWindowAt(ToPhysical(position));
        ShowSelection(_hover);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _dragStart = e.GetPosition(this);
        _dragging = false;
        CaptureMouse();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        ReleaseMouseCapture();
        if (_dragStart is not Point start)
        {
            return;
        }

        _dragStart = null;
        if (_dragging)
        {
            _dragging = false;
            Rectangle area = ToPhysical(new Rect(start, e.GetPosition(this)));
            if (area.Width >= MinimumSize && area.Height >= MinimumSize)
            {
                Finish(area);
            }

            return;
        }

        Finish(_hover ?? _screen.Bounds);
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        Finished?.Invoke(null);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Finished?.Invoke(null);
        }
        else if (e.Key == Key.Enter)
        {
            Finish(_screen.Bounds);
        }
    }

    private void Finish(Rectangle area) =>
        Finished?.Invoke(RecordingRegion.Create(_screen.DeviceName, area, _screen.Bounds));

    private Rectangle? FindWindowAt(System.Drawing.Point point)
    {
        foreach (Rectangle window in _windows)
        {
            if (window.Contains(point))
            {
                Rectangle clipped = Rectangle.Intersect(window, _screen.Bounds);
                return clipped.IsEmpty ? null : clipped;
            }
        }

        return null;
    }

    private double ScaleFactor => PhysicalWindow.Scale(this);

    private System.Drawing.Point ToPhysical(Point dip) =>
        new(_screen.Bounds.X + (int)Math.Round(dip.X * ScaleFactor), _screen.Bounds.Y + (int)Math.Round(dip.Y * ScaleFactor));

    private Rectangle ToPhysical(Rect dip)
    {
        System.Drawing.Point topLeft = ToPhysical(dip.TopLeft);
        System.Drawing.Point bottomRight = ToPhysical(dip.BottomRight);
        return Rectangle.FromLTRB(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
    }

    private Rect ToDip(Rectangle physical)
    {
        double scale = ScaleFactor;
        return new Rect((physical.X - _screen.Bounds.X) / scale, (physical.Y - _screen.Bounds.Y) / scale,
            physical.Width / scale, physical.Height / scale);
    }

    private void ShowSelection(Rectangle? physical)
    {
        var full = new Rect(0, 0, ActualWidth, ActualHeight);
        if (physical is not Rectangle area || area.IsEmpty)
        {
            Dim.Data = new RectangleGeometry(full);
            SelectionRect.Visibility = Visibility.Collapsed;
            SizeBadge.Visibility = Visibility.Collapsed;
            return;
        }

        Rect rect = ToDip(area);
        var geometry = new GeometryGroup { FillRule = FillRule.EvenOdd };
        geometry.Children.Add(new RectangleGeometry(full));
        geometry.Children.Add(new RectangleGeometry(rect));
        Dim.Data = geometry;

        SelectionRect.Margin = new Thickness(rect.X, rect.Y, 0, 0);
        SelectionRect.Width = rect.Width;
        SelectionRect.Height = rect.Height;
        SelectionRect.Visibility = Visibility.Visible;

        SizeText.Text = $"{area.Width & ~1} × {area.Height & ~1}";
        SizeBadge.Visibility = Visibility.Visible;
        SizeBadge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double badgeTop = rect.Y - SizeBadge.DesiredSize.Height - 4;
        SizeBadge.Margin = new Thickness(rect.X, badgeTop < 0 ? rect.Y + 4 : badgeTop, 0, 0);
    }
}
