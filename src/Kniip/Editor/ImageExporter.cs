using System;
using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kniip.Capture;
using Kniip.Infrastructure;
using Kniip.Interop;

namespace Kniip.Editor;

internal static class ImageExporter
{
    public static void Save(BitmapSource image, string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        BitmapEncoder encoder = ext is ".jpg" or ".jpeg"
            ? new JpegBitmapEncoder { QualityLevel = 92 }
            : new PngBitmapEncoder();

        BitmapSource frame = encoder is JpegBitmapEncoder
            ? new FormatConvertedBitmap(PixelUtil.OnWhite(image), PixelFormats.Bgr24, null, 0)
            : image;
        encoder.Frames.Add(BitmapFrame.Create(frame));

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
    }

    public static MemoryStream EncodePng(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        var stream = new MemoryStream();
        encoder.Save(stream);
        stream.Position = 0;
        return stream;
    }
}

internal static class ClipboardWriter
{
    /// <summary>
    /// Zet de afbeelding als bitmap én als PNG op het klembord, met een markering zodat
    /// de eigen klembordbewaking dit niet als nieuw knipsel ziet.
    /// Met een pad staat ook het bestand zelf op het klembord.
    /// De bitmap krijgt wit onder transparante delen (veel programma's lezen daar de alpha niet van); de PNG houdt ze transparant.
    /// </summary>
    public static bool SetImage(BitmapSource image, string? filePath = null)
    {
        var data = new DataObject();
        data.SetImage(PixelUtil.OnWhite(image));
        data.SetData("PNG", ImageExporter.EncodePng(image), false);
        data.SetData(ClipboardImageReader.ExportMarkerFormat, "1");
        if (!string.IsNullOrEmpty(filePath))
        {
            data.SetFileDropList(new StringCollection { filePath });
        }

        return TrySet(data);
    }

    /// <summary>Zet een opgeslagen bestand op het klembord, zodat het geplakt kan worden in Verkenner, Teams of Outlook.</summary>
    public static bool SetFile(string path)
    {
        var data = new DataObject();
        data.SetFileDropList(new StringCollection { path });
        return TrySet(data);
    }

    /// <summary>Alleen een eigen markering op het klembord, zonder afbeelding of tekst.</summary>
    public static bool SetMarker(string format, string value)
    {
        var data = new DataObject();
        data.SetData(format, value);
        return TrySet(data);
    }

    /// <summary>
    /// Tekst gaat rechtstreeks via Win32 op het klembord. De OLE-route van WPF moet het klembord
    /// daarna nog leegspoelen, en dat mislukt zodra een klembordbeheerder het meteen opent.
    /// </summary>
    public static bool SetText(string text)
    {
        if (TrySetUnicodeText(text))
        {
            return true;
        }

        var data = new DataObject();
        data.SetText(text);
        return TrySet(data);
    }

    private static HwndSource? _owner;

    private static IntPtr OwnerHandle
    {
        get
        {
            // Een venster als eigenaar is verplicht: zonder eigenaar weigert SetClipboardData na EmptyClipboard.
            _owner ??= new HwndSource(new HwndSourceParameters("KniipClipboardWriter")
            {
                ParentWindow = NativeMethods.HWND_MESSAGE,
                WindowStyle = 0
            });
            return _owner.Handle;
        }
    }

    private static bool TrySetUnicodeText(string text)
    {
        IntPtr owner = OwnerHandle;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            if (NativeMethods.OpenClipboard(owner))
            {
                try
                {
                    return NativeMethods.EmptyClipboard() && PutUnicodeText(text);
                }
                finally
                {
                    NativeMethods.CloseClipboard();
                }
            }

            Thread.Sleep(50);
        }

        return false;
    }

    private static bool PutUnicodeText(string text)
    {
        IntPtr handle = NativeMethods.GlobalAlloc(NativeMethods.GMEM_MOVEABLE, (UIntPtr)((text.Length + 1) * 2));
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        IntPtr target = NativeMethods.GlobalLock(handle);
        if (target == IntPtr.Zero)
        {
            NativeMethods.GlobalFree(handle);
            return false;
        }

        try
        {
            Marshal.Copy(text.ToCharArray(), 0, target, text.Length);
            Marshal.WriteInt16(target, text.Length * 2, 0);
        }
        finally
        {
            NativeMethods.GlobalUnlock(handle);
        }

        if (NativeMethods.SetClipboardData(NativeMethods.CF_UNICODETEXT, handle) == IntPtr.Zero)
        {
            NativeMethods.GlobalFree(handle);
            return false;
        }

        return true;
    }

    private static bool TrySet(DataObject data)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(data, true);
                return true;
            }
            catch (COMException)
            {
                Thread.Sleep(60);
            }
            catch (ExternalException)
            {
                Thread.Sleep(60);
            }
        }

        return false;
    }
}
