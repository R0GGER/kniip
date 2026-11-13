using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using Kniip.Infrastructure;

namespace Kniip.Capture;

internal static class ScreenGrabber
{
    /// <summary>Schermpixels in fysieke bureaubladcoördinaten (de app is per-monitor DPI-bewust).</summary>
    public static BitmapSource Capture(Rectangle bounds)
    {
        using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
        }

        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, bounds.Width, bounds.Height), ImageLockMode.ReadOnly,
            PixelFormat.Format32bppRgb);
        var pixels = new byte[bounds.Width * bounds.Height * 4];
        try
        {
            for (int y = 0; y < bounds.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * bounds.Width * 4, bounds.Width * 4);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        for (int i = 3; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;
        }

        return PixelUtil.CreateBitmap(pixels, bounds.Width, bounds.Height);
    }

    /// <summary>Deel van een eerder vastgelegd scherm; <paramref name="area"/> is relatief aan dat scherm.</summary>
    public static BitmapSource Crop(BitmapSource source, Int32Rect area)
    {
        int stride = area.Width * 4;
        var pixels = new byte[stride * area.Height];
        source.CopyPixels(area, pixels, stride, 0);
        return PixelUtil.CreateBitmap(pixels, area.Width, area.Height);
    }
}
