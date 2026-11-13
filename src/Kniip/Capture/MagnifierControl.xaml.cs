using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Kniip.Capture;

/// <summary>Rond vergrootglas met kruisdraad en coördinaten dat naast de muisaanwijzer meeloopt.</summary>
public partial class MagnifierControl : UserControl
{
    private const int Pixels = 15;
    private const double CursorOffset = 20;

    private static MagnifierControl? _visible;

    private readonly WriteableBitmap _zoom = new(Pixels, Pixels, 96, 96, PixelFormats.Bgra32, null);
    private readonly byte[] _buffer = new byte[Pixels * Pixels * 4];
    private readonly TranslateTransform _offset = new();

    public MagnifierControl()
    {
        InitializeComponent();
        Zoom.Source = _zoom;
        RenderTransform = _offset;
        Visibility = Visibility.Collapsed;
        Unloaded += (_, _) => Hide();
    }

    /// <param name="frozen">Vastgelegd beeld van het scherm.</param>
    /// <param name="relative">Muispositie in pixels van <paramref name="frozen"/>.</param>
    /// <param name="screen">Muispositie in fysieke bureaubladcoördinaten, voor de tekst.</param>
    /// <param name="cursor">Muispositie in het venster (DIP).</param>
    /// <param name="area">Grootte van het venster (DIP).</param>
    public void Show(BitmapSource frozen, System.Drawing.Point relative, System.Drawing.Point screen, Point cursor, Size area)
    {
        // Elk scherm heeft een eigen overlay; een overlay krijgt soms muisbewegingen terwijl de aanwijzer op een ander scherm staat.
        if (relative.X < 0 || relative.Y < 0 || relative.X >= frozen.PixelWidth || relative.Y >= frozen.PixelHeight)
        {
            Hide();
            return;
        }

        if (_visible != this)
        {
            _visible?.Hide();
            _visible = this;
        }

        CopyPixels(frozen, relative);
        CoordinateText.Text = $"X: {screen.X} Y: {screen.Y}";
        Visibility = Visibility.Visible;

        Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Size size = DesiredSize;
        double left = cursor.X + CursorOffset;
        if (left + size.Width > area.Width)
        {
            left = cursor.X - CursorOffset - size.Width;
        }

        double top = cursor.Y + CursorOffset;
        if (top + size.Height > area.Height)
        {
            top = cursor.Y - CursorOffset - size.Height;
        }

        _offset.X = Math.Max(0, left);
        _offset.Y = Math.Max(0, top);
    }

    public void Hide()
    {
        Visibility = Visibility.Collapsed;
        if (_visible == this)
        {
            _visible = null;
        }
    }

    private void CopyPixels(BitmapSource frozen, System.Drawing.Point center)
    {
        Array.Clear(_buffer);
        for (int i = 3; i < _buffer.Length; i += 4)
        {
            _buffer[i] = 255;
        }

        int half = Pixels / 2;
        int x0 = Math.Max(0, center.X - half);
        int y0 = Math.Max(0, center.Y - half);
        int x1 = Math.Min(frozen.PixelWidth, center.X + half + 1);
        int y1 = Math.Min(frozen.PixelHeight, center.Y + half + 1);
        if (x1 > x0 && y1 > y0)
        {
            int offset = ((y0 - (center.Y - half)) * Pixels + (x0 - (center.X - half))) * 4;
            frozen.CopyPixels(new Int32Rect(x0, y0, x1 - x0, y1 - y0), _buffer, Pixels * 4, offset);
        }

        _zoom.WritePixels(new Int32Rect(0, 0, Pixels, Pixels), _buffer, Pixels * 4, 0);
    }
}
