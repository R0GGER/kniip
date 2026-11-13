using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace Kniip.Recording;

/// <summary>MP4 inkorten via Windows.Media.Editing (zelfde resolutie, codec en geluid als de opname).</summary>
internal static class VideoExporter
{
    public static async Task TrimMp4Async(string source, string target, TimeSpan start, TimeSpan end, TimeSpan duration,
        IProgress<double>? progress, CancellationToken cancellationToken)
    {
        string folderPath = Path.GetDirectoryName(Path.GetFullPath(target))!;
        Directory.CreateDirectory(folderPath);

        bool fullRange = start <= TimeSpan.FromMilliseconds(50) && end >= duration - TimeSpan.FromMilliseconds(50);
        if (fullRange)
        {
            await Task.Run(() => File.Copy(source, target, overwrite: true), cancellationToken);
            progress?.Report(1);
            return;
        }

        StorageFile input = await StorageFile.GetFileFromPathAsync(source);
        MediaClip clip = await MediaClip.CreateFromFileAsync(input);
        clip.TrimTimeFromStart = start;
        clip.TrimTimeFromEnd = clip.OriginalDuration > end ? clip.OriginalDuration - end : TimeSpan.Zero;
        var composition = new MediaComposition();
        composition.Clips.Add(clip);

        MediaEncodingProfile profile = await MediaEncodingProfile.CreateFromFileAsync(input);
        if (profile.Video is { } video)
        {
            // Een opname van een stilstaand scherm heeft een lage gemiddelde bitrate; die zou beweging wazig maken.
            uint minimum = (uint)Math.Min(20_000_000, video.Width * video.Height * 3L);
            video.Bitrate = Math.Max(video.Bitrate, minimum);
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
                throw new InvalidOperationException($"Windows kon de video niet opslaan ({result}).");
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
}
