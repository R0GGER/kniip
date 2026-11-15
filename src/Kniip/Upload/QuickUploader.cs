using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Kniip.Capture;
using Kniip.Editor;
using Kniip.History;
using Kniip.Resources;
using Kniip.Settings;

namespace Kniip.Upload;

/// <summary>Uploadt bestanden of de inhoud van het klembord zonder editor en zet de link(s) op het klembord.</summary>
internal static class QuickUploader
{
    /// <param name="Open">Opent de inhoud; pas vlak voor het uploaden, zodat grote bestanden niet allemaal tegelijk openstaan.</param>
    private sealed record Item(string FileName, string ContentType, UploadKind Kind, HistoryKind HistoryKind,
        Func<Stream> Open, string? FilePath, Func<BitmapSource?> Preview);

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".bmp"] = "image/bmp",
        [".webp"] = "image/webp",
        [".tif"] = "image/tiff",
        [".tiff"] = "image/tiff",
        [".ico"] = "image/x-icon",
        [".heic"] = "image/heic",
        [".svg"] = "image/svg+xml",
        [".mp4"] = "video/mp4",
        [".m4v"] = "video/x-m4v",
        [".mov"] = "video/quicktime",
        [".webm"] = "video/webm",
        [".mkv"] = "video/x-matroska",
        [".avi"] = "video/x-msvideo",
        [".wmv"] = "video/x-ms-wmv",
        [".txt"] = "text/plain",
        [".log"] = "text/plain",
        [".md"] = "text/markdown",
        [".csv"] = "text/csv",
        [".html"] = "text/html",
        [".htm"] = "text/html",
        [".json"] = "application/json",
        [".xml"] = "application/xml",
        [".pdf"] = "application/pdf",
        [".zip"] = "application/zip",
        [".7z"] = "application/x-7z-compressed",
        [".mp3"] = "audio/mpeg",
        [".wav"] = "audio/wav",
    };

    private static bool _busy;

    public static Task UploadFilesAsync(IReadOnlyList<string> paths) => RunAsync(() => FileItems(paths));

    public static Task UploadClipboardAsync() => RunAsync(ClipboardItems);

    private static async Task RunAsync(Func<List<Item>> collect)
    {
        App app = App.Instance;
        if (_busy)
        {
            app.ShowBalloon(Strings.Upload_QuickTitle, Strings.Upload_QuickBusy);
            return;
        }

        AppSettings settings = app.Settings;
        if (settings.DefaultUploader == null)
        {
            app.ShowBalloon(Strings.Upload_QuickTitle, Strings.Upload_QuickNoDestination);
            return;
        }

        List<Item> items = collect();
        if (items.Count == 0)
        {
            app.ShowBalloon(Strings.Upload_QuickTitle, Strings.Upload_QuickNothing);
            return;
        }

        _busy = true;
        var urls = new List<string>();
        int failed = 0;
        string? error = null;
        bool changed = false;
        try
        {
            app.ShowBalloon(Strings.Upload_QuickTitle, items.Count == 1
                ? string.Format(Strings.Upload_QuickUploadingOne, items[0].FileName)
                : string.Format(Strings.Upload_QuickUploadingMany, items.Count));
            foreach (Item item in items)
            {
                try
                {
                    UploadDestination destination = settings.UploaderFor(item.Kind) ?? throw new UploadException(Strings.Upload_NotConfigured);
                    UploadResult result;
                    await using (Stream stream = item.Open())
                    {
                        result = await UploaderFactory.UploadAsync(destination, stream, item.FileName, item.ContentType);
                    }

                    changed |= result.DestinationChanged;
                    urls.Add(result.Url);
                    HistoryStore.Add(item.HistoryKind, item.FilePath, result.Url, item.Preview());
                }
                catch (OperationCanceledException)
                {
                    failed++;
                }
                catch (Exception ex)
                {
                    failed++;
                    error = ex.Message;
                }
            }
        }
        finally
        {
            _busy = false;
        }

        if (changed)
        {
            app.SaveSettings();
        }

        Report(urls, failed, error);
    }

    private static void Report(List<string> urls, int failed, string? error)
    {
        App app = App.Instance;
        if (urls.Count == 0)
        {
            app.ShowBalloon(Strings.Upload_QuickFailedTitle, error == null
                ? Strings.Upload_QuickCancelled
                : string.Format(Strings.Common_UploadFailed, error));
            return;
        }

        bool copied = ClipboardWriter.SetText(string.Join(Environment.NewLine, urls));
        string text = !copied
            ? string.Format(Strings.Upload_QuickCopyFailed, urls[0])
            : urls.Count == 1
                ? string.Format(Strings.Upload_QuickCopiedOne, urls[0])
                : string.Format(Strings.Upload_QuickCopiedMany, urls.Count);
        if (failed > 0)
        {
            text += "\n" + string.Format(Strings.Upload_QuickPartlyFailed, failed, error ?? Strings.Upload_QuickCancelled);
        }

        app.ShowBalloon(Strings.Upload_QuickDoneTitle, text);
    }

    private static List<Item> FileItems(IEnumerable<string> paths) =>
        paths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).Select(FileItem).ToList();

    private static Item FileItem(string path)
    {
        string contentType = ContentTypes.TryGetValue(Path.GetExtension(path), out string? known) ? known : "application/octet-stream";
        bool image = contentType.StartsWith("image/", StringComparison.Ordinal);
        bool video = contentType.StartsWith("video/", StringComparison.Ordinal);
        HistoryKind historyKind = image ? HistoryKind.Image : video ? HistoryKind.Video : HistoryKind.File;
        return new Item(Path.GetFileName(path), contentType, image || video ? UploadKind.Media : UploadKind.File, historyKind,
            () => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true), path,
            () => image ? HistoryStore.PreviewOf(path) : null);
    }

    /// <summary>Wat er op het klembord staat, in volgorde van voorkeur: bestanden, een afbeelding, tekst.</summary>
    private static List<Item> ClipboardItems()
    {
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        try
        {
            if (Clipboard.ContainsFileDropList())
            {
                return FileItems(Clipboard.GetFileDropList().Cast<string>());
            }

            if (ClipboardImageReader.TryRead() is { } image)
            {
                return new List<Item>
                {
                    new($"kniip-{stamp}.png", "image/png", UploadKind.Media, HistoryKind.Image,
                        () => ImageExporter.EncodePng(image), null, () => image)
                };
            }

            if (Clipboard.ContainsText() && Clipboard.GetText() is { Length: > 0 } text && !string.IsNullOrWhiteSpace(text))
            {
                // Met BOM: zonder charset in het content-type lezen browsers de tekst anders niet als UTF-8.
                byte[] bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray();
                return new List<Item>
                {
                    new($"kniip-{stamp}.txt", "text/plain", UploadKind.Text, HistoryKind.Text,
                        () => new MemoryStream(bytes), null, () => null)
                };
            }
        }
        catch (ExternalException)
        {
            // Klembord bezet: als leeg behandelen.
        }

        return new List<Item>();
    }
}
