using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Kniip.Recording;

/// <summary>Zet (een deel van) een MP4 om naar een geanimeerde GIF.</summary>
internal static class GifExporter
{
    public static Task ExportAsync(string source, string target, TimeSpan start, TimeSpan end, int fps, int maxWidth,
        IProgress<double>? progress, CancellationToken cancellationToken) =>
        Task.Run(() => Export(source, target, start, end, fps, maxWidth, progress, cancellationToken), cancellationToken);

    private static void Export(string source, string target, TimeSpan start, TimeSpan end, int fps, int maxWidth,
        IProgress<double>? progress, CancellationToken cancellationToken)
    {
        fps = Math.Clamp(fps, 1, 50);
        using var reader = new VideoFrameReader(source);
        (int width, int height) = TargetSize(reader.Width, reader.Height, maxWidth);
        TimeSpan step = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / fps);
        if (end <= start)
        {
            end = start + step;
        }

        string temp = target + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
            using (var encoder = new GifEncoder(stream, width, height))
            {
                var writer = new FrameWriter(encoder, start);
                VideoFrame? current = null;
                TimeSpan nextTarget = start;

                foreach (VideoFrame frame in reader.ReadFrames(start, cancellationToken))
                {
                    // Het frame dat op tijdstip t zichtbaar is, is het laatste frame met een tijd <= t.
                    while (nextTarget < end && frame.Time > nextTarget)
                    {
                        writer.Show(current ?? frame, nextTarget, width, height);
                        nextTarget += step;
                    }

                    current = frame;
                    if (nextTarget >= end)
                    {
                        break;
                    }

                    progress?.Report(Math.Clamp((frame.Time - start).TotalMilliseconds / (end - start).TotalMilliseconds, 0, 1));
                }

                while (current != null && nextTarget < end)
                {
                    writer.Show(current, nextTarget, width, height);
                    nextTarget += step;
                }

                writer.Complete(end);
                encoder.Finish();
            }

            File.Move(temp, target, overwrite: true);
            progress?.Report(1);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    private static (int Width, int Height) TargetSize(int width, int height, int maxWidth)
    {
        if (maxWidth <= 0 || width <= maxWidth)
        {
            return (width, height);
        }

        return (maxWidth, Math.Max(1, (int)Math.Round(height * (double)maxWidth / width)));
    }

    /// <summary>Voegt opeenvolgende identieke frames samen tot één frame met een langere vertraging.</summary>
    private sealed class FrameWriter
    {
        private readonly GifEncoder _encoder;
        private readonly TimeSpan _origin;
        private VideoFrame? _source;
        private byte[]? _pending;
        private TimeSpan _pendingStart;
        private int _writtenCentiseconds;

        public FrameWriter(GifEncoder encoder, TimeSpan origin)
        {
            _encoder = encoder;
            _origin = origin;
        }

        public void Show(VideoFrame frame, TimeSpan time, int width, int height)
        {
            if (ReferenceEquals(frame, _source))
            {
                return;
            }

            byte[] pixels = Scale(frame, width, height);
            _source = frame;
            if (_pending != null && pixels.AsSpan().SequenceEqual(_pending))
            {
                return;
            }

            Flush(time);
            _pending = pixels;
            _pendingStart = time;
        }

        public void Complete(TimeSpan end) => Flush(end);

        private void Flush(TimeSpan until)
        {
            if (_pending == null)
            {
                return;
            }

            // Afronden op de tijdlijn in plaats van per frame, zodat afrondingsfouten zich niet opstapelen.
            int endCentiseconds = (int)Math.Round((until - _origin).TotalMilliseconds / 10);
            int delay = Math.Max(1, endCentiseconds - _writtenCentiseconds);
            _encoder.AddFrame(_pending, delay);
            _writtenCentiseconds += delay;
            _pending = null;
        }

        private static byte[] Scale(VideoFrame frame, int width, int height) =>
            frame.Width == width && frame.Height == height
                ? frame.Pixels
                : BoxScale(frame.Pixels, frame.Width, frame.Height, width, height);

        /// <summary>Verkleinen door per doelpixel het gemiddelde van het bronvlak te nemen.</summary>
        private static byte[] BoxScale(byte[] source, int sourceWidth, int sourceHeight, int width, int height)
        {
            var result = new byte[width * height * 4];
            double scaleX = (double)sourceWidth / width;
            double scaleY = (double)sourceHeight / height;
            for (int y = 0; y < height; y++)
            {
                int y0 = (int)(y * scaleY);
                int y1 = Math.Max(y0 + 1, Math.Min(sourceHeight, (int)Math.Ceiling((y + 1) * scaleY)));
                for (int x = 0; x < width; x++)
                {
                    int x0 = (int)(x * scaleX);
                    int x1 = Math.Max(x0 + 1, Math.Min(sourceWidth, (int)Math.Ceiling((x + 1) * scaleX)));
                    int b = 0, g = 0, r = 0, count = 0;
                    for (int sy = y0; sy < y1; sy++)
                    {
                        int row = sy * sourceWidth * 4;
                        for (int sx = x0; sx < x1; sx++)
                        {
                            int i = row + sx * 4;
                            b += source[i];
                            g += source[i + 1];
                            r += source[i + 2];
                            count++;
                        }
                    }

                    int o = (y * width + x) * 4;
                    result[o] = (byte)(b / count);
                    result[o + 1] = (byte)(g / count);
                    result[o + 2] = (byte)(r / count);
                    result[o + 3] = 255;
                }
            }

            return result;
        }
    }
}
