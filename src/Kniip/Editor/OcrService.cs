using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Globalization;
using Windows.Media.Ocr;
using BitmapAlphaMode = Windows.Graphics.Imaging.BitmapAlphaMode;
using BitmapPixelFormat = Windows.Graphics.Imaging.BitmapPixelFormat;
using SoftwareBitmap = Windows.Graphics.Imaging.SoftwareBitmap;
using WinBitmapDecoder = Windows.Graphics.Imaging.BitmapDecoder;
using Kniip.Resources;

namespace Kniip.Editor;

/// <summary>Tekst herkennen met de OCR die in Windows zit; de taal volgt de talen van het gebruikersprofiel.</summary>
internal static class OcrService
{
    /// <summary>Kleinere afbeeldingen worden vergroot: kleine schermtekst mist de OCR anders vaak.</summary>
    private const int UpscaleBelowPixels = 2_000_000;

    /// <summary>Herkende regels, gescheiden door regeleinden; leeg als er geen tekst is gevonden.</summary>
    /// <exception cref="InvalidOperationException">Er is geen taal voor tekstherkenning geïnstalleerd.</exception>
    public static async Task<string> RecognizeAsync(BitmapSource image)
    {
        OcrEngine engine = CreateEngine();
        using SoftwareBitmap bitmap = await ToSoftwareBitmapAsync(Prepare(image));
        OcrResult result = await engine.RecognizeAsync(bitmap);
        return Layout(result);
    }

    /// <summary>
    /// Regels opnieuw opbouwen uit de plek van elk woord. Windows geeft de tekst per blok terug; bij afwisselende
    /// kleuren of opmaak valt één regel dan in stukken die ver uit elkaar in het resultaat staan.
    /// </summary>
    private static string Layout(OcrResult result)
    {
        var words = result.Lines
            .SelectMany(line => line.Words)
            .Select(word => (word.Text, Box: new Rect(word.BoundingRect.X, word.BoundingRect.Y, word.BoundingRect.Width, word.BoundingRect.Height)))
            .Where(word => word.Text.Length > 0 && word.Box.Height > 0)
            .OrderBy(word => word.Box.Top + word.Box.Height / 2)
            .ToList();

        var rows = new List<Row>();
        foreach ((string text, Rect box) in words)
        {
            double center = box.Top + box.Height / 2;
            Row? row = rows.LastOrDefault(r => center >= r.Top && center <= r.Bottom
                                               && Math.Abs(r.Center - center) <= Math.Min(r.Height, box.Height) * 0.6);
            if (row == null)
            {
                row = new Row();
                rows.Add(row);
            }

            row.Add(text, box);
        }

        rows.Sort((a, b) => a.Center.CompareTo(b.Center));
        double lineHeight = rows.Count == 0 ? 0 : rows.Select(r => r.Height).OrderBy(h => h).ElementAt(rows.Count / 2);
        var output = new StringBuilder();
        for (int i = 0; i < rows.Count; i++)
        {
            if (i > 0)
            {
                output.AppendLine();
                // Een duidelijk grotere witruimte dan een gewone regelafstand: een lege regel tussen alinea's.
                if (rows[i].Top - rows[i - 1].Bottom > lineHeight * 0.9)
                {
                    output.AppendLine();
                }
            }

            output.Append(rows[i].Text);
        }

        return output.ToString();
    }

    private sealed class Row
    {
        private readonly List<(string Text, Rect Box)> _words = new();

        public double Top { get; private set; } = double.MaxValue;

        public double Bottom { get; private set; } = double.MinValue;

        public double Height => Bottom - Top;

        public double Center => (Top + Bottom) / 2;

        public void Add(string text, Rect box)
        {
            _words.Add((text, box));
            Top = Math.Min(Top, box.Top);
            Bottom = Math.Max(Bottom, box.Bottom);
        }

        public string Text => string.Join(" ", _words.OrderBy(w => w.Box.Left).Select(w => w.Text));
    }

    private static OcrEngine CreateEngine()
    {
        if (OcrEngine.TryCreateFromUserProfileLanguages() is { } engine)
        {
            return engine;
        }

        var english = new Language("en-US");
        if (OcrEngine.IsLanguageSupported(english) && OcrEngine.TryCreateFromLanguage(english) is { } fallback)
        {
            return fallback;
        }

        throw new InvalidOperationException(
            Strings.Ocr_NoLanguage);
    }

    /// <summary>Op wit leggen (transparant leest de OCR als zwart) en zo nodig vergroten of verkleinen.</summary>
    private static BitmapSource Prepare(BitmapSource image)
    {
        int width = image.PixelWidth, height = image.PixelHeight;
        long pixels = (long)width * height;
        double scale = pixels < UpscaleBelowPixels / 4 ? 3 : pixels < UpscaleBelowPixels ? 2 : 1;
        double max = OcrEngine.MaxImageDimension;
        scale = Math.Min(scale, Math.Min(max / width, max / height));
        int w = Math.Max(1, (int)Math.Round(width * scale));
        int h = Math.Max(1, (int)Math.Round(height * scale));

        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            var area = new Rect(0, 0, w, h);
            dc.DrawRectangle(Brushes.White, null, area);
            dc.DrawImage(image, area);
        }

        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        var target = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    private static async Task<SoftwareBitmap> ToSoftwareBitmapAsync(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var png = new MemoryStream();
        encoder.Save(png);
        png.Position = 0;

        using var stream = png.AsRandomAccessStream();
        WinBitmapDecoder decoder = await WinBitmapDecoder.CreateAsync(stream);
        return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
    }
}
