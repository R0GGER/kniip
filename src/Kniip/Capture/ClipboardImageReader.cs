using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using Kniip.Infrastructure;

namespace Kniip.Capture;

/// <summary>
/// Leest afbeeldingen robuust van het klembord. <see cref="Clipboard.GetImage"/> van WPF gaat vaak mis
/// met 32-bit DIB's, dus eerst "PNG", dan zelf de DIB decoderen, en pas als laatste de standaard-API.
/// </summary>
internal static class ClipboardImageReader
{
    public const string ExportMarkerFormat = "Kniip.Export";

    public static bool IsOwnExport()
    {
        try
        {
            return Clipboard.ContainsData(ExportMarkerFormat);
        }
        catch (COMException)
        {
            return false;
        }
    }

    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".ico", ".webp", ".heic" };

    /// <param name="includeFiles">Ook een in Verkenner gekopieerd afbeeldingsbestand openen
    /// (niet voor de klembordbewaking, anders opent elk gekopieerd bestand de editor).</param>
    public static BitmapSource? TryRead(bool includeFiles = false)
    {
        try
        {
            IDataObject? data = Clipboard.GetDataObject();
            if (data == null)
            {
                return null;
            }

            if (ReadData(data, includeFiles) is { } image)
            {
                return image;
            }

            if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } bitmap)
            {
                return OpaqueCopy(bitmap);
            }
        }
        catch (Exception ex) when (ex is COMException or ExternalException or NotSupportedException or FileFormatException)
        {
            return null;
        }

        return null;
    }

    /// <summary>Snelle controle tijdens het slepen, zonder te decoderen.</summary>
    public static bool CanDrop(IDataObject data)
    {
        try
        {
            return data.GetDataPresent("PNG") || data.GetDataPresent(DataFormats.Dib) || data.GetDataPresent(DataFormats.Bitmap)
                || (data.GetData(DataFormats.FileDrop) is string[] files && Array.Exists(files, IsImageFile));
        }
        catch (Exception ex) when (ex is COMException or ExternalException)
        {
            return false;
        }
    }

    /// <summary>Afbeelding uit iets dat in kniip is neergezet: een afbeeldingsbestand of afbeeldingsdata.</summary>
    public static BitmapSource? FromDrop(IDataObject data)
    {
        try
        {
            if (ReadData(data, includeFiles: true) is { } image)
            {
                return image;
            }

            return data.GetData(DataFormats.Bitmap) is BitmapSource bitmap ? OpaqueCopy(bitmap) : null;
        }
        catch (Exception ex) when (ex is COMException or ExternalException or NotSupportedException or FileFormatException)
        {
            return null;
        }
    }

    private static BitmapSource? ReadData(IDataObject data, bool includeFiles)
    {
        if (includeFiles && TryReadFile(data) is { } fromFile)
        {
            return fromFile;
        }

        if (data.GetDataPresent("PNG") && data.GetData("PNG") is MemoryStream png)
        {
            return PixelUtil.Decode(png);
        }

        if (data.GetDataPresent(DataFormats.Dib) && data.GetData(DataFormats.Dib) is MemoryStream dib)
        {
            return FromDib(dib.ToArray());
        }

        return null;
    }

    private static BitmapSource OpaqueCopy(BitmapSource image) =>
        PixelUtil.CreateBitmap(PixelUtil.ToOpaqueBgra(image), image.PixelWidth, image.PixelHeight);

    private static bool IsImageFile(string file) =>
        Array.IndexOf(ImageExtensions, Path.GetExtension(file).ToLowerInvariant()) >= 0;

    private static BitmapSource? TryReadFile(IDataObject data)
    {
        if (!data.GetDataPresent(DataFormats.FileDrop) || data.GetData(DataFormats.FileDrop) is not string[] files)
        {
            return null;
        }

        foreach (string file in files)
        {
            if (!File.Exists(file) || !IsImageFile(file))
            {
                continue;
            }

            try
            {
                return PixelUtil.LoadFile(file);
            }
            catch (Exception)
            {
                // Onleesbaar bestand: volgende proberen.
            }
        }

        return null;
    }

    private static BitmapSource? FromDib(byte[] dib)
    {
        if (dib.Length < 40)
        {
            return null;
        }

        int headerSize = BitConverter.ToInt32(dib, 0);
        int bitCount = BitConverter.ToInt16(dib, 14);
        int compression = BitConverter.ToInt32(dib, 16);
        int colorsUsed = BitConverter.ToInt32(dib, 32);

        const int BI_BITFIELDS = 3;
        int masks = compression == BI_BITFIELDS && headerSize == 40 ? 12 : 0;
        int colors = colorsUsed != 0 ? colorsUsed : bitCount <= 8 ? 1 << bitCount : 0;
        int pixelOffset = 14 + headerSize + masks + colors * 4;

        var file = new byte[14 + dib.Length];
        file[0] = (byte)'B';
        file[1] = (byte)'M';
        BitConverter.GetBytes(file.Length).CopyTo(file, 2);
        BitConverter.GetBytes(pixelOffset).CopyTo(file, 10);
        Buffer.BlockCopy(dib, 0, file, 14, dib.Length);

        try
        {
            BitmapSource image = PixelUtil.Decode(new MemoryStream(file));
            return PixelUtil.IsFullyTransparent(image) ? OpaqueCopy(image) : image;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
