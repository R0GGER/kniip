using System;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Kniip.Infrastructure;

internal static class PixelUtil
{
    /// <summary>
    /// Zet een afbeelding om naar Bgra32 met volledig dekkende pixels.
    /// Klembord-DIB's hebben vaak alpha 0; screenshots zijn altijd ondoorzichtig.
    /// </summary>
    public static byte[] ToOpaqueBgra(BitmapSource source)
    {
        BitmapSource converted = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

        int stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        for (int i = 3; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;
        }

        return pixels;
    }

    /// <summary>Zet een afbeelding om naar Bgra32 en behoudt de transparantie.</summary>
    public static byte[] ToBgra(BitmapSource source)
    {
        BitmapSource converted = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

        int stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    public static bool HasTransparency(byte[] bgra)
    {
        for (int i = 3; i < bgra.Length; i += 4)
        {
            if (bgra[i] != 255)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Een afbeelding waarvan alle pixels alpha 0 hebben, is vrijwel altijd een DIB zonder echte alpha.
    /// </summary>
    public static bool IsFullyTransparent(BitmapSource source)
    {
        if (!source.Format.Equals(PixelFormats.Bgra32) && !source.Format.Equals(PixelFormats.Pbgra32))
        {
            return false;
        }

        byte[] pixels = ToBgra(source);
        for (int i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Transparante delen wit maken, voor formaten en programma's zonder alpha (JPEG, bitmap op het klembord).</summary>
    public static BitmapSource OnWhite(BitmapSource image)
    {
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            var area = new System.Windows.Rect(0, 0, image.PixelWidth, image.PixelHeight);
            dc.DrawRectangle(Brushes.White, null, area);
            dc.DrawImage(image, area);
        }

        var bitmap = new RenderTargetBitmap(image.PixelWidth, image.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var opaque = new FormatConvertedBitmap(bitmap, PixelFormats.Bgr32, null, 0);
        opaque.Freeze();
        return opaque;
    }

    public static BitmapSource CreateBitmap(byte[] pixels, int width, int height)
    {
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    public static string ComputeHash(BitmapSource source)
    {
        byte[] pixels = ToOpaqueBgra(source);
        byte[] hash = SHA256.HashData(pixels);
        return $"{source.PixelWidth}x{source.PixelHeight}:{Convert.ToHexString(hash)}";
    }

    public static BitmapSource LoadFile(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        return Decode(new MemoryStream(bytes));
    }

    public static BitmapSource Decode(Stream stream)
    {
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        BitmapFrame frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }
}
