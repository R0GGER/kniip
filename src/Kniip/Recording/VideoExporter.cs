using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Kniip.Resources;
using Kniip.Watermark;

namespace Kniip.Recording;

/// <summary>MP4 inkorten via Windows.Media.Editing (zelfde resolutie, codec en geluid als de opname).</summary>
internal static class VideoExporter
{
    /// <param name="watermark">Komt als overlay over de hele video; de video wordt dan altijd opnieuw gecodeerd. Null voor geen watermerk.</param>
    public static async Task TrimMp4Async(string source, string target, TimeSpan start, TimeSpan end, TimeSpan duration,
        IProgress<double>? progress, CancellationToken cancellationToken, WatermarkSettings? watermark = null)
    {
        string folderPath = Path.GetDirectoryName(Path.GetFullPath(target))!;
        Directory.CreateDirectory(folderPath);

        bool fullRange = start <= TimeSpan.FromMilliseconds(50) && end >= duration - TimeSpan.FromMilliseconds(50);
        if (fullRange && watermark == null)
        {
            await Task.Run(() => File.Copy(source, target, overwrite: true), cancellationToken);
            progress?.Report(1);
            return;
        }

        StorageFile input = await StorageFile.GetFileFromPathAsync(source);
        MediaClip clip = await MediaClip.CreateFromFileAsync(input);
        if (!fullRange)
        {
            clip.TrimTimeFromStart = start;
            clip.TrimTimeFromEnd = clip.OriginalDuration > end ? clip.OriginalDuration - end : TimeSpan.Zero;
        }

        var composition = new MediaComposition();
        composition.Clips.Add(clip);

        MediaEncodingProfile profile = await MediaEncodingProfile.CreateFromFileAsync(input);
        if (profile.Video is { } video)
        {
            // Een opname van een stilstaand scherm heeft een lage gemiddelde bitrate; die zou beweging wazig maken.
            uint minimum = (uint)Math.Min(20_000_000, video.Width * video.Height * 3L);
            video.Bitrate = Math.Max(video.Bitrate, minimum);
        }

        string? overlayFile = null;
        try
        {
            if (watermark != null && profile.Video is { Width: > 0, Height: > 0 } size)
            {
                overlayFile = await AddWatermarkAsync(composition, clip.TrimmedDuration, (int)size.Width, (int)size.Height, watermark.Clone(),
                    cancellationToken);
            }

            StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(folderPath);
            StorageFile output = await folder.CreateFileAsync(Path.GetFileName(target), CreationCollisionOption.ReplaceExisting);
            var percent = new Progress<double>(value => progress?.Report(value / 100));
            try
            {
                TranscodeFailureReason result = await composition
                    .RenderToFileAsync(output, MediaTrimmingPreference.Precise, profile)
                    .AsTask(cancellationToken, percent);
                if (result != TranscodeFailureReason.None)
                {
                    throw new InvalidOperationException(string.Format(Strings.Video_SaveFailed, result));
                }
            }
            catch
            {
                try
                {
                    File.Delete(target);
                }
                catch (Exception)
                {
                    // Half bestand laten staan is beter dan de oorspronkelijke fout verbergen.
                }

                throw;
            }
        }
        finally
        {
            if (overlayFile != null)
            {
                try
                {
                    File.Delete(overlayFile);
                }
                catch (Exception)
                {
                    // Blijft in de tijdelijke map staan; die wordt bij de volgende start opgeruimd.
                }
            }
        }
    }

    /// <summary>Het watermerk als transparante afbeelding over het hele beeld, de hele duur; geeft het tijdelijke bestand terug.</summary>
    private static async Task<string?> AddWatermarkAsync(MediaComposition composition, TimeSpan length, int width, int height,
        WatermarkSettings watermark, CancellationToken cancellationToken)
    {
        string folderPath = Path.Combine(RecordingSession.TempFolder, "Watermark");
        Directory.CreateDirectory(folderPath);
        string path = Path.Combine(folderPath, Guid.NewGuid().ToString("N")[..8] + ".png");
        if (!await Task.Run(() => WatermarkRenderer.SaveOverlayPng(width, height, watermark, path), cancellationToken))
        {
            return null;
        }

        StorageFile image = await StorageFile.GetFileFromPathAsync(path);
        MediaClip still = await MediaClip.CreateFromImageFileAsync(image, length);
        var overlay = new MediaOverlay(still)
        {
            Position = new Windows.Foundation.Rect(0, 0, width, height),
            Opacity = 1
        };
        var layer = new MediaOverlayLayer();
        layer.Overlays.Add(overlay);
        composition.OverlayLayers.Add(layer);
        return path;
    }
}
