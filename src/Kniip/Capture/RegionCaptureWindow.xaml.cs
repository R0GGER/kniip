using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kniip.Recording;
using Rectangle = System.Drawing.Rectangle;
using Screen = System.Windows.Forms.Screen;

namespace Kniip.Capture;

/// <summary>
/// Bevroren beeld van één beeldscherm waarop je een rechthoek sleept. Er staat er één per scherm;
/// een knipsel ligt daardoor altijd binnen één scherm.
/// </summary>
public partial class RegionCaptureWindow : Window
{
    private const double DragThreshold = 3;
    private const int MinimumSize = 3;

    private readonly Screen _screen;
    private readonly BitmapSource _frozen;
    private readonly IReadOnlyList<Rectangle>? _windows;
    private readonly bool _showMagnifier;
    private Point? _dragStart;
    private bool _dragging;
    private Rectangle? _hover;

    /// <summary>Het knipsel, of null bij annuleren.</summary>
    internal event Action<BitmapSource?>? Finished;

    /// <summary>Geknipt gebied in fysieke schermcoördinaten, gezet vlak voor Finished.</summary>
    internal Rectangle? SelectedArea { get; private set; }

    /// <param name="windows">Vensterrechthoeken van boven naar onder; gezet = venster aanklikken in plaats van slepen.</param>
    /// <param name="showMagnifier">Vergrootglas bij de muisaanwijzer tonen; niet bij venster aanklikken.</param>
    internal RegionCaptureWindow(Screen screen, BitmapSource frozen, IReadOnlyList<Rectangle>? windows = null, bool showMagnifier = false)
    {
        InitializeComponent();
        _screen = screen;
        _frozen = frozen;
        _windows = windows;
        _showMagnifier = showMagnifier && windows == null;
        Frozen.Source = frozen;
        MouseLeave += (_, _) => Magnifier.Hide();
        if (windows != null)
        {
            Cursor = Cursors.Hand;
            HintTitle.Text = "Knipsel: klik op een venster";
            MouseLeave += (_, _) =>
            {
                _hover = null;
                ShowSelection(null);
            };
        }

        SourceInitialized += (_, _) => PhysicalWindow.Place(this, screen.Bounds);
        Loaded += (_, _) => ShowSelection(null);
    }

    private bool PicksWindow => _windows != null;

    public bool ContainsCursor(System.Drawing.Point cursor) => _screen.Bounds.Contains(cursor);

    public void Cancel() => Finished?.Invoke(null);

    public void SelectWholeScreen() => Finish(_screen.Bounds);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (PicksWindow)
        {
            Rectangle? hover = FindWindowAt(ToPhysical(e.GetPosition(this)));
            if (hover != _hover)
            {
                _hover = hover;
                ShowSelection(hover);
            }

            return;
        }

        Point position = e.GetPosition(this);
        if (_showMagnifier)
        {
            System.Drawing.Point physical = ToPhysical(position);
            var relative = new System.Drawing.Point(physical.X - _screen.Bounds.X, physical.Y - _screen.Bounds.Y);
            Magnifier.Show(_frozen, relative, physical, position, new Size(ActualWidth, ActualHeight));
        }

        if (_dragStart is not Point start || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (!_dragging && (position - start).Length >= DragThreshold)
        {
            _dragging = true;
            Hint.Visibility = Visibility.Collapsed;
        }

        if (_dragging)
        {
            ShowSelection(ToPhysical(new Rect(start, position)));
        }
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

        if (PicksWindow)
        {
            _dragStart = null;
            Finish(FindWindowAt(ToPhysical(e.GetPosition(this))) ?? _screen.Bounds);
            return;
        }

        _dragStart = null;
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        Rectangle area = ToPhysical(new Rect(start, e.GetPosition(this)));
        if (area.Width >= MinimumSize && area.Height >= MinimumSize)
        {
            Finish(area);
        }
        else
        {
            ShowSelection(null);
        }
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        Cancel();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Cancel();
        }
        else if (e.Key == Key.Enter)
        {
            SelectWholeScreen();
        }
    }

    private void Finish(Rectangle area)
    {
        Rectangle clipped = Rectangle.Intersect(area, _screen.Bounds);
        if (clipped.Width <= 0 || clipped.Height <= 0)
        {
            Cancel();
            return;
        }

        var relative = new Int32Rect(clipped.X - _screen.Bounds.X, clipped.Y - _screen.Bounds.Y, clipped.Width, clipped.Height);
        SelectedArea = clipped;
        Finished?.Invoke(ScreenGrabber.Crop(_frozen, relative));
    }

    private Rectangle? FindWindowAt(System.Drawing.Point point)
    {
        foreach (Rectangle window in _windows ?? Array.Empty<Rectangle>())
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

        SizeText.Text = $"{area.Width} × {area.Height}";
        SizeBadge.Visibility = Visibility.Visible;
        SizeBadge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double badgeTop = rect.Y - SizeBadge.DesiredSize.Height - 4;
        SizeBadge.Margin = new Thickness(rect.X, badgeTop < 0 ? rect.Y + 4 : badgeTop, 0, 0);
    }
}
