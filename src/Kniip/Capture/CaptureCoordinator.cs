using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Kniip.Infrastructure;
using Kniip.Interop;
using Kniip.Settings;

namespace Kniip.Capture;

/// <summary>
/// Combineert klembord- en mapbewaking. Het Knipprogramma kopieert én bewaart elk knipsel,
/// dus dubbele meldingen van hetzelfde knipsel worden hier weggefilterd.
/// </summary>
internal sealed class CaptureCoordinator : IDisposable
{
    private static readonly TimeSpan HashWindow = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SameSizeWindow = TimeSpan.FromSeconds(4);

    private readonly ClipboardWatcher _clipboard;
    private readonly FolderWatcher _folder;
    private readonly List<RecentCapture> _recent = new();
    private readonly ConcurrentDictionary<string, DateTime> _ignoredPaths = new(StringComparer.OrdinalIgnoreCase);

    public event Action<BitmapSource>? CaptureReceived;

    public bool Paused { get; set; }

    public static string? ScreenshotsFolder
    {
        get
        {
            string? path = NativeMethods.GetKnownFolderPath(NativeMethods.FOLDERID_Screenshots);
            if (path != null)
            {
                return path;
            }

            string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            return pictures.Length == 0 ? null : Path.Combine(pictures, "Screenshots");
        }
    }

    public CaptureCoordinator(Dispatcher dispatcher)
    {
        _clipboard = new ClipboardWatcher();
        _clipboard.ImageCaptured += OnImage;

        _folder = new FolderWatcher(dispatcher) { ShouldIgnore = IsIgnored };
        _folder.ImageCaptured += (image, _) => OnImage(image);
    }

    public void ApplySettings(AppSettings settings)
    {
        _clipboard.Enabled = settings.WatchClipboard;
        _clipboard.OnlyFromSnippingTool = settings.OnlyFromSnippingTool;
        _folder.Enabled = settings.WatchScreenshotsFolder;
    }

    /// <summary>Bestanden die kniip zelf opslaat niet opnieuw oppikken.</summary>
    public void IgnorePath(string path) => _ignoredPaths[Path.GetFullPath(path)] = DateTime.UtcNow;

    private bool IsIgnored(string path)
    {
        DateTime now = DateTime.UtcNow;
        foreach (KeyValuePair<string, DateTime> entry in _ignoredPaths)
        {
            if (now - entry.Value > TimeSpan.FromMinutes(2))
            {
                _ignoredPaths.TryRemove(entry.Key, out _);
            }
        }

        return _ignoredPaths.ContainsKey(Path.GetFullPath(path));
    }

    private void OnImage(BitmapSource image)
    {
        if (Paused)
        {
            return;
        }

        DateTime now = DateTime.UtcNow;
        _recent.RemoveAll(r => now - r.Time > HashWindow);

        string hash = PixelUtil.ComputeHash(image);
        foreach (RecentCapture recent in _recent)
        {
            bool sameContent = recent.Hash == hash;
            bool sameSizeJustNow = recent.Width == image.PixelWidth && recent.Height == image.PixelHeight
                                   && now - recent.Time < SameSizeWindow;
            if (sameContent || sameSizeJustNow)
            {
                return;
            }
        }

        _recent.Add(new RecentCapture(hash, image.PixelWidth, image.PixelHeight, now));
        CaptureReceived?.Invoke(image);
    }

    public void Dispose()
    {
        _clipboard.Dispose();
        _folder.Dispose();
    }

    private readonly record struct RecentCapture(string Hash, int Width, int Height, DateTime Time);
}
