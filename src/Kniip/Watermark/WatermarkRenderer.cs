using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kniip.Infrastructure;

namespace Kniip.Watermark;

/// <summary>
/// Tekent een watermerk; afbeeldingen, de editor, GIF-frames en de MP4-overlay gebruiken allemaal deze code,
/// zodat het er overal hetzelfde uitziet.
/// </summary>
public static class WatermarkRenderer
{
    private const double TextEmSize = 100;

    /// <summary>Bovengrens voor het aantal herhalingen, zodat een heel klein watermerk het tekenen niet vastzet.</summary>
    private const int MaxPatternItems = 2500;

    private static readonly Brush ShadowBrush = Freeze(new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)));
    private static readonly object CacheLock = new();
    private static (string Path, DateTime Stamp, BitmapSource Image)? _image;
    private static (string Key, Content? Content)? _content;

    /// <summary>De inhoud op zijn natuurlijke grootte; <see cref="Bounds"/> is het deel dat er echt getekend wordt.</summary>
    private sealed record Content(Drawing Drawing, Rect Bounds);

    /// <summary>Het watermerk heeft iets om te tekenen: tekst, of een afbeelding die te laden is.</summary>
    public static bool IsUsable(WatermarkSettings settings) => GetContent(settings) != null;

    /// <summary>Laadt de afbeelding van het watermerk; null als het bestand ontbreekt of onleesbaar is.</summary>
    public static BitmapSource? LoadImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            DateTime stamp = File.GetLastWriteTimeUtc(path);
            lock (CacheLock)
            {
                if (_image is { } cached && cached.Path == path && cached.Stamp == stamp)
                {
                    return cached.Image;
                }
            }

            BitmapSource image = PixelUtil.LoadFile(path);
            lock (CacheLock)
            {
                _image = (path, stamp, image);
            }

            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Kader van één watermerk op de ingestelde plek; null als er niets te tekenen is.</summary>
    public static Rect? PlacedRect(Size target, WatermarkSettings settings)
    {
        if (GetContent(settings) is not { } content)
        {
            return null;
        }

        Size item = ItemSize(target, content.Bounds.Size, settings, fit: true);
        double margin = Math.Min(target.Width, target.Height) * settings.MarginPercent / 100;
        int column = (int)settings.Position % 3;
        int row = (int)settings.Position / 3;
        double x = column switch
        {
            0 => margin,
            1 => (target.Width - item.Width) / 2,
            _ => target.Width - item.Width - margin
        };
        double y = row switch
        {
            0 => margin,
            1 => (target.Height - item.Height) / 2,
            _ => target.Height - item.Height - margin
        };
        return new Rect(x, y, item.Width, item.Height);
    }

    /// <summary>Breedte en hoogte bij de ingestelde grootte, met de verhouding van de inhoud.</summary>
    public static Size? ItemSize(Size target, WatermarkSettings settings) =>
        GetContent(settings) is { } content ? ItemSize(target, content.Bounds.Size, settings, fit: !settings.Pattern) : null;

    /// <summary>De verhouding breedte/hoogte van de inhoud; null als er niets te tekenen is.</summary>
    public static double? AspectRatio(WatermarkSettings settings) =>
        GetContent(settings) is { } content ? content.Bounds.Width / content.Bounds.Height : null;

    private static Size ItemSize(Size target, Size natural, WatermarkSettings settings, bool fit)
    {
        double width = Math.Max(1, target.Width * settings.SizePercent / 100);
        double height = width * natural.Height / natural.Width;
        if (fit && height > target.Height)
        {
            height = Math.Max(1, target.Height);
            width = height * natural.Width / natural.Height;
        }

        return new Size(width, height);
    }

    /// <summary>Het hele watermerk volgens de instellingen: één keer op zijn plek of als patroon.</summary>
    public static void Draw(DrawingContext dc, Size target, WatermarkSettings settings)
    {
        if (settings.Pattern)
        {
            DrawPattern(dc, target, settings);
        }
        else if (PlacedRect(target, settings) is Rect rect)
        {
            DrawAt(dc, settings, rect, 0);
        }
    }

    /// <summary>Eén watermerk in <paramref name="rect"/>, gedraaid rond het midden.</summary>
    public static void DrawAt(DrawingContext dc, WatermarkSettings settings, Rect rect, double angle)
    {
        if (GetContent(settings) is not { } content || rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        dc.PushOpacity(settings.Opacity / 100.0);
        DrawContent(dc, content, rect, angle);
        dc.Pop();
    }

    /// <summary>Het watermerk herhaald over het hele beeld, in een gedraaid raster met verspringende rijen.</summary>
    public static void DrawPattern(DrawingContext dc, Size target, WatermarkSettings settings)
    {
        if (GetContent(settings) is not { } content)
        {
            return;
        }

        dc.PushClip(new RectangleGeometry(new Rect(target)));
        dc.PushOpacity(settings.Opacity / 100.0);
        foreach (Rect rect in PatternRects(target, settings))
        {
            DrawContent(dc, content, rect, settings.PatternAngle);
        }

        dc.Pop();
        dc.Pop();
    }

    /// <summary>De (ongedraaide) kaders van de herhalingen; elk kader wordt rond zijn midden gedraaid met <see cref="WatermarkSettings.PatternAngle"/>.</summary>
    public static IEnumerable<Rect> PatternRects(Size target, WatermarkSettings settings)
    {
        if (GetContent(settings) is not { } content || target.Width <= 0 || target.Height <= 0)
        {
            yield break;
        }

        Size item = ItemSize(target, content.Bounds.Size, settings, fit: false);
        double gap = settings.PatternSpacing / 100;
        double stepX = Math.Max(4, item.Width * (1 + gap));
        double stepY = Math.Max(4, item.Height * (1 + gap) + item.Width * gap * 0.25);
        double radius = Math.Sqrt(target.Width * target.Width + target.Height * target.Height) / 2 + Math.Max(item.Width, item.Height);
        int columns = (int)Math.Ceiling(radius / stepX) + 1;
        int rows = (int)Math.Ceiling(radius / stepY) + 1;
        if ((2L * columns + 1) * (2L * rows + 1) > MaxPatternItems * 4L)
        {
            yield break;
        }

        var center = new Point(target.Width / 2, target.Height / 2);
        var rotation = new RotateTransform(settings.PatternAngle);
        int count = 0;
        for (int row = -rows; row <= rows; row++)
        {
            double shift = (row & 1) == 0 ? 0 : stepX / 2;
            for (int column = -columns; column <= columns; column++)
            {
                Point local = rotation.Transform(new Point(column * stepX + shift, row * stepY));
                var middle = new Point(center.X + local.X, center.Y + local.Y);
                double reach = Math.Max(item.Width, item.Height);
                if (middle.X < -reach || middle.Y < -reach || middle.X > target.Width + reach || middle.Y > target.Height + reach)
                {
                    continue;
                }

                if (++count > MaxPatternItems)
                {
                    yield break;
                }

                yield return new Rect(middle.X - item.Width / 2, middle.Y - item.Height / 2, item.Width, item.Height);
            }
        }
    }

    private static void DrawContent(DrawingContext dc, Content content, Rect rect, double angle)
    {
        var matrix = Matrix.Identity;
        matrix.Translate(-content.Bounds.X, -content.Bounds.Y);
        matrix.Scale(rect.Width / content.Bounds.Width, rect.Height / content.Bounds.Height);
        matrix.Translate(rect.X, rect.Y);
        if (angle != 0)
        {
            matrix.RotateAt(angle, rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        }

        dc.PushTransform(new MatrixTransform(matrix));
        dc.DrawDrawing(content.Drawing);
        dc.Pop();
    }

    /// <summary>Een kopie van de afbeelding met het watermerk erop, op dezelfde resolutie.</summary>
    public static BitmapSource Apply(BitmapSource source, WatermarkSettings settings)
    {
        int width = source.PixelWidth;
        int height = source.PixelHeight;
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        TextOptions.SetTextRenderingMode(visual, TextRenderingMode.Grayscale);
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawImage(source, new Rect(0, 0, width, height));
            Draw(dc, new Size(width, height), settings);
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// Alleen het watermerk op een transparant vlak, als voorvermenigvuldigde BGRA-pixels; null als er niets te tekenen is.
    /// Mag vanaf elke thread; het tekenen gebeurt zo nodig op een eigen STA-thread.
    /// </summary>
    public static byte[]? RenderOverlay(int width, int height, WatermarkSettings settings) =>
        RunSta(() =>
        {
            if (!IsUsable(settings))
            {
                return null;
            }

            BitmapSource bitmap = RenderOverlayBitmap(width, height, settings);
            var pixels = new byte[width * height * 4];
            bitmap.CopyPixels(pixels, width * 4, 0);
            return pixels;
        });

    /// <summary>Schrijft alleen het watermerk als transparante PNG; false als er niets te tekenen is. Mag vanaf elke thread.</summary>
    public static bool SaveOverlayPng(int width, int height, WatermarkSettings settings, string path) =>
        RunSta(() =>
        {
            if (!IsUsable(settings))
            {
                return false;
            }

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(RenderOverlayBitmap(width, height, settings)));
            using FileStream stream = File.Create(path);
            encoder.Save(stream);
            return true;
        });

    /// <summary>Het watermerk als transparante afbeelding, bijvoorbeeld als voorbeeld over een video.</summary>
    public static BitmapSource RenderOverlayBitmap(int width, int height, WatermarkSettings settings)
    {
        var visual = new DrawingVisual();
        TextOptions.SetTextRenderingMode(visual, TextRenderingMode.Grayscale);
        using (DrawingContext dc = visual.RenderOpen())
        {
            Draw(dc, new Size(width, height), settings);
        }

        var bitmap = new RenderTargetBitmap(Math.Max(1, width), Math.Max(1, height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Legt een overlay van <see cref="RenderOverlay"/> over een ondoorzichtig BGRA-frame van dezelfde grootte.</summary>
    public static void Blend(byte[] frame, byte[] overlay)
    {
        int length = Math.Min(frame.Length, overlay.Length);
        for (int i = 0; i < length; i += 4)
        {
            int alpha = overlay[i + 3];
            if (alpha == 0)
            {
                continue;
            }

            int rest = 255 - alpha;
            frame[i] = (byte)(overlay[i] + (frame[i] * rest + 127) / 255);
            frame[i + 1] = (byte)(overlay[i + 1] + (frame[i + 1] * rest + 127) / 255);
            frame[i + 2] = (byte)(overlay[i + 2] + (frame[i + 2] * rest + 127) / 255);
        }
    }

    private static Content? GetContent(WatermarkSettings settings)
    {
        string key = string.Join("\u001F", settings.Kind, settings.Text, settings.FontFamily, settings.Bold, settings.Color, settings.Shadow,
            settings.ImagePath, settings.Kind == WatermarkKind.Image && File.Exists(settings.ImagePath)
                ? File.GetLastWriteTimeUtc(settings.ImagePath).Ticks
                : 0);
        lock (CacheLock)
        {
            if (_content is { } cached && cached.Key == key)
            {
                return cached.Content;
            }
        }

        Content? content = CreateContent(settings);
        lock (CacheLock)
        {
            _content = (key, content);
        }

        return content;
    }

    private static Content? CreateContent(WatermarkSettings settings)
    {
        var group = new DrawingGroup();
        RenderOptions.SetBitmapScalingMode(group, BitmapScalingMode.HighQuality);
        if (settings.Kind == WatermarkKind.Image)
        {
            if (LoadImage(settings.ImagePath) is not { } image || image.PixelWidth == 0 || image.PixelHeight == 0)
            {
                return null;
            }

            group.Children.Add(new ImageDrawing(image, new Rect(0, 0, image.PixelWidth, image.PixelHeight)));
        }
        else
        {
            string text = (settings.Text ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                return null;
            }

            var typeface = new Typeface(new FontFamily(string.IsNullOrWhiteSpace(settings.FontFamily) ? "Segoe UI" : settings.FontFamily),
                FontStyles.Normal, settings.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
            var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, TextEmSize,
                Brushes.Black, 1.0) { TextAlignment = TextAlignment.Center };
            Geometry geometry = formatted.BuildGeometry(new Point(0, 0));
            if (geometry.Bounds.IsEmpty)
            {
                return null;
            }

            if (settings.Shadow)
            {
                Geometry shadow = geometry.Clone();
                shadow.Transform = new TranslateTransform(TextEmSize * 0.03, TextEmSize * 0.03);
                group.Children.Add(new GeometryDrawing(ShadowBrush, null, shadow));
            }

            group.Children.Add(new GeometryDrawing(Freeze(new SolidColorBrush(settings.Color)), null, geometry));
        }

        group.Freeze();
        Rect bounds = group.Bounds;
        return bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0 ? null : new Content(group, bounds);
    }

    private static T RunSta<T>(Func<T> work)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            return work();
        }

        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null)
        {
            throw new InvalidOperationException(error.Message, error);
        }

        return result;
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
