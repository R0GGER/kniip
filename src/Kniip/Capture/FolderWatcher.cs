using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Kniip.Infrastructure;

namespace Kniip.Capture;

/// <summary>
/// Bewaakt de map Schermafbeeldingen, waar het Knipprogramma knipsels automatisch opslaat.
/// </summary>
internal sealed class FolderWatcher : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private FileSystemWatcher? _watcher;

    public event Action<BitmapSource, string>? ImageCaptured;

    public Func<string, bool>? ShouldIgnore { get; set; }

    public FolderWatcher(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public bool Enabled
    {
        get => _watcher?.EnableRaisingEvents == true;
        set
        {
            if (value)
            {
                Start();
            }
            else if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
            }
        }
    }

    private void Start()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = true;
            return;
        }

        string? folder = CaptureCoordinator.ScreenshotsFolder;
        if (folder == null || !Directory.Exists(folder))
        {
            return;
        }

        _watcher = new FileSystemWatcher(folder)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName
        };
        _watcher.Created += (_, e) => OnFileAppeared(e.FullPath);
        _watcher.Renamed += (_, e) => OnFileAppeared(e.FullPath);
        _watcher.EnableRaisingEvents = true;
    }

    private void OnFileAppeared(string path)
    {
        string ext = Path.GetExtension(path);
        if (!ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
            && !ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            && !ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (ShouldIgnore?.Invoke(path) == true)
        {
            return;
        }

        Task.Run(async () =>
        {
            for (int attempt = 0; attempt < 25; attempt++)
            {
                await Task.Delay(200);
                try
                {
                    BitmapSource image = PixelUtil.LoadFile(path);
                    await _dispatcher.InvokeAsync(() => ImageCaptured?.Invoke(image, path));
                    return;
                }
                catch (IOException)
                {
                    // Bestand wordt nog geschreven.
                }
                catch (UnauthorizedAccessException)
                {
                }
                catch (Exception)
                {
                    // Nog onvolledig of geen geldige afbeelding; opnieuw proberen.
                }
            }
        });
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;
    }
}
