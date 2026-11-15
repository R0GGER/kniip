using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Kniip.Editor;
using Kniip.History;
using Kniip.Recording;
using Kniip.Resources;
using Kniip.Settings;
using Kniip.Upload;

namespace Kniip.Workflows;

/// <summary>
/// Opslaan en uploaden van knipsels en screencasts zonder venster; de knoppen in de editor en de screencast
/// gebruiken dezelfde stappen als de workflows.
/// </summary>
internal static class WorkflowActions
{
    public static string SnipDefaultName => string.Format(Strings.Snip_DefaultName, DateTime.Now);

    public static string VideoDefaultName => string.Format(Strings.Screencast_DefaultName, DateTime.Now);

    public static WindowInfo SnipTemplateWindow(WindowInfo? window) => window ?? new WindowInfo(Strings.Snip_Name, Strings.Snip_Name);

    public static WindowInfo VideoTemplateWindow(WindowInfo? window) => window ?? new WindowInfo(Strings.Screencast_Name, Strings.Screencast_Name);

    public static string VideoExtension(ScreencastFormat format) => format == ScreencastFormat.Gif ? ".gif" : ".mp4";

    public static string VideoContentType(ScreencastFormat format) => format == ScreencastFormat.Gif ? "image/gif" : "video/mp4";

    /// <summary>Pad in de opslagmap voor knipsels; null als er geen opslagmap is (dan vragen).</summary>
    /// <exception cref="Exception">De map of het sjabloon is onbruikbaar.</exception>
    public static string? SnipTarget(AppSettings settings, WindowInfo? window) =>
        settings.SnipSaveFolder is { Length: > 0 } folder
            ? FileNameTemplate.Resolve(folder, settings.SnipFolderTemplate, settings.SnipFileNameTemplate, SnipTemplateWindow(window), SnipDefaultName, ".png")
            : null;

    /// <summary>Pad in de opslagmap voor screencasts; null als er geen opslagmap is (dan vragen).</summary>
    /// <exception cref="Exception">De map of het sjabloon is onbruikbaar.</exception>
    public static string? VideoTarget(AppSettings settings, WindowInfo? window, ScreencastFormat format) =>
        settings.VideoSaveFolder is { Length: > 0 } folder
            ? FileNameTemplate.Resolve(folder, settings.VideoFolderTemplate, settings.VideoFileNameTemplate, VideoTemplateWindow(window),
                VideoDefaultName, VideoExtension(format))
            : null;

    /// <summary>Schrijft het knipsel als PNG en zet het in de geschiedenis.</summary>
    public static void SaveSnip(BitmapSource image, string path)
    {
        App.Instance.Capture.IgnorePath(path);
        ImageExporter.Save(image, path);
        HistoryStore.Add(HistoryKind.Snip, path, null, image);
    }

    /// <summary>Uploadt het knipsel als PNG, zet de link in de geschiedenis en geeft hem terug.</summary>
    public static async Task<string> UploadSnipAsync(UploadDestination destination, BitmapSource image, string fileName)
    {
        UploadResult result;
        using (MemoryStream png = ImageExporter.EncodePng(image))
        {
            result = await UploaderFactory.UploadAsync(destination, png, fileName, "image/png");
        }

        if (result.DestinationChanged)
        {
            App.Instance.SaveSettings();
        }

        HistoryStore.Add(HistoryKind.Snip, null, result.Url, image);
        return result.Url;
    }

    /// <summary>Uploadt een geëxporteerde screencast, zet de link in de geschiedenis en geeft hem terug.</summary>
    public static async Task<string> UploadVideoAsync(UploadDestination destination, string path, ScreencastFormat format,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        UploadResult result;
        await using (FileStream stream = File.OpenRead(path))
        {
            result = await UploaderFactory.UploadAsync(destination, stream, Path.GetFileName(path), VideoContentType(format), progress, cancellationToken);
        }

        if (result.DestinationChanged)
        {
            App.Instance.SaveSettings();
        }

        AddVideoHistory(format, null, result.Url, path);
        return result.Url;
    }

    /// <param name="exported">Het geëxporteerde bestand, voor de miniatuur van een GIF.</param>
    public static void AddVideoHistory(ScreencastFormat format, string? file, string? url, string exported)
    {
        bool gif = format == ScreencastFormat.Gif;
        HistoryStore.Add(gif ? HistoryKind.Gif : HistoryKind.Mp4, file, url, gif ? HistoryStore.PreviewOf(exported) : null);
    }

    /// <summary>Een hele opname zonder inkorten als MP4 (kopie) of GIF (met de ingestelde fps en breedte) wegschrijven.</summary>
    public static async Task ExportRecordingAsync(string recording, string target, ScreencastFormat format, AppSettings settings,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
        if (format == ScreencastFormat.Mp4)
        {
            await Task.Run(() => File.Copy(recording, target, overwrite: true), cancellationToken);
            return;
        }

        TimeSpan duration;
        using (var reader = new VideoFrameReader(recording))
        {
            duration = reader.Duration;
        }

        await GifExporter.ExportAsync(recording, target, TimeSpan.Zero, duration, settings.GifFps, settings.GifMaxWidth, progress, cancellationToken);
    }

    /// <summary>Nieuwe, lege map in de tijdelijke map van de screencasts.</summary>
    public static string NewTempFolder(string kind)
    {
        string folder = Path.Combine(RecordingSession.TempFolder, kind, Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        return folder;
    }
}
