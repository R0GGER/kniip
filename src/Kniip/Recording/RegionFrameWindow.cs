using System;
using System.Windows;
using System.Windows.Media;
using Kniip.Interop;
using Rectangle = System.Drawing.Rectangle;
using Shape = System.Windows.Shapes.Rectangle;

namespace Kniip.Recording;

/// <summary>Rand net buiten het opnamegebied; klikken gaan erdoorheen en hij komt niet in de opname.</summary>
internal sealed class RegionFrameWindow : Window
{
    private const double ThicknessDip = 2;

    private static readonly Brush ReadyBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x00, 0x67, 0xC0)));
    private static readonly Brush RecordingBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xE8, 0x11, 0x23)));

    private readonly Rectangle _region;
    private readonly Shape _frame = new() { StrokeDashArray = new DoubleCollection { 4, 2 } };

    public RegionFrameWindow(Rectangle region)
    {
        _region = region;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Content = _frame;
        SetRecording(false);

        SourceInitialized += (_, _) =>
        {
            IntPtr hwnd = PhysicalWindow.Handle(this);
            NativeMethods.AddExtendedStyle(hwnd,
                NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);
            PhysicalWindow.ExcludeFromCapture(this);
            UpdatePlacement();
        };
        DpiChanged += (_, _) => UpdatePlacement();
    }

    public void SetRecording(bool recording) => _frame.Stroke = recording ? RecordingBrush : ReadyBrush;

    private void UpdatePlacement()
    {
        double scale = PhysicalWindow.Scale(this);
        int border = (int)Math.Ceiling(ThicknessDip * scale);
        _frame.StrokeThickness = border / scale;
        Rectangle outer = _region;
        outer.Inflate(border, border);
        PhysicalWindow.Place(this, outer);
    }

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
