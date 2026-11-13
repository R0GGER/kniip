using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Kniip.Editor;
using Kniip.Settings;
using Kniip.Upload;
using Microsoft.Win32;
using Kniip.Resources;

namespace Kniip.Recording;

/// <summary>Nabewerking van een screencast: bekijken, begin en einde inkorten en opslaan als MP4 of GIF.</summary>
public partial class ScreencastWindow : Window
{
    private static readonly TimeSpan MinimumLength = TimeSpan.FromMilliseconds(200);
    private static readonly int[] FpsChoices = { 5, 10, 15, 20, 25 };
    private static readonly int[] WidthChoices = { 0, 1920, 1280, 960, 800, 640, 480 };

    private enum DragTarget
    {
        None,
        Start,
        End,
        Playhead
    }

    private readonly string _path;
    private readonly AppSettings _settings;
    private readonly string? _windowName;
    private readonly DispatcherTimer _playTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private TimeSpan _duration;
    private TimeSpan _start;
    private TimeSpan _end;
    private bool _playing;
    private bool _loaded;
    private DragTarget _drag;
    private CancellationTokenSource? _export;
    private bool _kept;
    private string? _lastSaved;

    /// <param name="windowName">Programmanaam van het opgenomen venster, voor %V in het naamsjabloon.</param>
    internal ScreencastWindow(string path, AppSettings settings, ScreencastFormat? format = null, string? windowName = null)
    {
        InitializeComponent();
        Infrastructure.ThemeManager.Attach(this);
        _path = path;
        _settings = settings;
        _windowName = windowName;

        int[] fpsChoices = FpsChoices.Contains(settings.GifFps) ? FpsChoices : FpsChoices.Append(settings.GifFps).Order().ToArray();
        foreach (int fps in fpsChoices)
        {
            AddChoice(FpsCombo, fps.ToString(CultureInfo.CurrentCulture), fps, fps == settings.GifFps);
        }

        int[] widths = WidthChoices.Contains(settings.GifMaxWidth)
            ? WidthChoices
            : WidthChoices.Append(settings.GifMaxWidth).OrderByDescending(w => w == 0 ? int.MaxValue : w).ToArray();
        foreach (int width in widths)
        {
            AddChoice(WidthCombo, width == 0 ? Strings.Screencast_Original : $"{width} px", width, width == settings.GifMaxWidth);
        }

        FpsCombo.SelectionChanged += (_, _) => UpdateInfo();
        WidthCombo.SelectionChanged += (_, _) => UpdateInfo();
        SetFormat(format ?? settings.LastScreencastFormat);

        _playTimer.Tick += (_, _) => OnPlayTick();
        Player.Source = new Uri(path);
        Player.Play();
    }

    private static void AddChoice(ComboBox combo, string text, int value, bool selected)
    {
        var item = new ComboBoxItem { Content = text, Tag = value };
        combo.Items.Add(item);
        if (selected)
        {
            combo.SelectedItem = item;
        }
    }

    private ScreencastFormat Format => GifToggle.IsChecked == true ? ScreencastFormat.Gif : ScreencastFormat.Mp4;

    private int GifFps => FpsCombo.SelectedItem is ComboBoxItem { Tag: int fps } ? fps : 10;

    private int GifWidth => WidthCombo.SelectedItem is ComboBoxItem { Tag: int width } ? width : 800;

    private bool IsBusy => _export != null;

    // ---------- Afspelen ----------

    private void Player_MediaOpened(object sender, RoutedEventArgs e)
    {
        Player.Pause();
        _duration = Player.NaturalDuration.HasTimeSpan ? Player.NaturalDuration.TimeSpan : TimeSpan.Zero;
        _start = TimeSpan.Zero;
        _end = _duration;
        _loaded = true;
        Player.Position = TimeSpan.Zero;
        PlayerMessage.Visibility = Visibility.Collapsed;
        UpdateTrim();
        UpdateInfo();
    }

    private void Player_MediaFailed(object? sender, ExceptionRoutedEventArgs e)
    {
        PlayerMessage.Text = string.Format(Strings.Screencast_PreviewFailed, e.ErrorException.Message);
        PlayerMessage.Visibility = Visibility.Visible;
        _loaded = false;
        using var reader = TryOpenReader();
        _duration = reader?.Duration ?? TimeSpan.Zero;
        _start = TimeSpan.Zero;
        _end = _duration;
        UpdateTrim();
        UpdateInfo();
    }

    private VideoFrameReader? TryOpenReader()
    {
        try
        {
            return new VideoFrameReader(_path);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void Player_MediaEnded(object sender, RoutedEventArgs e) => SetPlaying(false);

    private void Play_Click(object sender, RoutedEventArgs e) => TogglePlay();

    private void Video_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => TogglePlay();

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Space && Keyboard.FocusedElement is not ComboBox)
        {
            TogglePlay();
            e.Handled = true;
        }
    }

    private void TogglePlay()
    {
        if (!_loaded)
        {
            return;
        }

        if (_playing)
        {
            SetPlaying(false);
            return;
        }

        TimeSpan position = Player.Position;
        if (position < _start || position >= _end - TimeSpan.FromMilliseconds(50))
        {
            Player.Position = _start;
        }

        SetPlaying(true);
    }

    private void SetPlaying(bool playing)
    {
        _playing = playing;
        if (playing)
        {
            Player.Play();
            _playTimer.Start();
        }
        else
        {
            Player.Pause();
            _playTimer.Stop();
        }

        PlayIcon.Text = playing ? "\uE769" : "\uE768";
        UpdatePlayhead();
    }

    private void OnPlayTick()
    {
        if (Player.Position >= _end)
        {
            SetPlaying(false);
            Player.Position = _end;
        }

        UpdatePlayhead();
    }

    private void Seek(TimeSpan time)
    {
        Player.Position = Clamp(time, TimeSpan.Zero, _duration);
        UpdatePlayhead();
    }

    // ---------- Inkortbalk ----------

    private double HandleWidth => StartHandle.Width;

    private double TrackWidth => Math.Max(1, TrimArea.ActualWidth - 2 * HandleWidth);

    private double ToX(TimeSpan time) =>
        HandleWidth + (_duration.Ticks == 0 ? 0 : TrackWidth * time.Ticks / _duration.Ticks);

    private TimeSpan ToTime(double x) =>
        _duration.Ticks == 0 ? TimeSpan.Zero : TimeSpan.FromTicks((long)(Math.Clamp((x - HandleWidth) / TrackWidth, 0, 1) * _duration.Ticks));

    private void TrimArea_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateTrim();

    private void TrimArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_duration == TimeSpan.Zero || IsBusy)
        {
            return;
        }

        double x = e.GetPosition(TrimArea).X;
        double startX = ToX(_start) - HandleWidth / 2;
        double endX = ToX(_end) + HandleWidth / 2;
        if (Math.Abs(x - startX) <= HandleWidth)
        {
            _drag = DragTarget.Start;
        }
        else if (Math.Abs(x - endX) <= HandleWidth)
        {
            _drag = DragTarget.End;
        }
        else
        {
            _drag = DragTarget.Playhead;
        }

        if (_drag != DragTarget.Playhead)
        {
            SetPlaying(false);
        }

        TrimArea.CaptureMouse();
        ApplyDrag(x);
        e.Handled = true;
    }

    private void TrimArea_MouseMove(object sender, MouseEventArgs e)
    {
        if (_drag != DragTarget.None)
        {
            ApplyDrag(e.GetPosition(TrimArea).X);
        }
    }

    private void TrimArea_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _drag = DragTarget.None;
        TrimArea.ReleaseMouseCapture();
    }

    private void ApplyDrag(double x)
    {
        switch (_drag)
        {
            case DragTarget.Start:
                _start = Clamp(ToTime(x + HandleWidth / 2), TimeSpan.Zero, _end - MinimumLength);
                Seek(_start);
                break;
            case DragTarget.End:
                _end = Clamp(ToTime(x - HandleWidth / 2), _start + MinimumLength, _duration);
                Seek(_end);
                break;
            case DragTarget.Playhead:
                Seek(ToTime(x));
                break;
        }

        UpdateTrim();
    }

    private void SetStart_Click(object sender, RoutedEventArgs e)
    {
        _start = Clamp(Player.Position, TimeSpan.Zero, _end - MinimumLength);
        UpdateTrim();
    }

    private void SetEnd_Click(object sender, RoutedEventArgs e)
    {
        _end = Clamp(Player.Position, _start + MinimumLength, _duration);
        UpdateTrim();
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max) =>
        max < min ? min : value < min ? min : value > max ? max : value;

    private void UpdateTrim()
    {
        double height = TrimArea.ActualHeight;
        TrackBorder.Width = Math.Max(0, TrimArea.ActualWidth);
        TrackBorder.Height = height;

        double startX = ToX(_start);
        double endX = ToX(_end);
        Canvas.SetLeft(StartHandle, startX - HandleWidth);
        Canvas.SetLeft(EndHandle, endX);
        Canvas.SetLeft(SelectionBorder, startX);
        SelectionBorder.Width = Math.Max(0, endX - startX);

        SelectionText.Text = string.Format(Strings.Screencast_Selection, FormatTime(_start), FormatTime(_end), (_end - _start).TotalSeconds);
        UpdatePlayhead();
        UpdateInfo();
    }

    private void UpdatePlayhead()
    {
        TimeSpan position = _loaded ? Player.Position : TimeSpan.Zero;
        Canvas.SetLeft(Playhead, ToX(position) - Playhead.Width / 2);
        PositionText.Text = $"{FormatTime(position)} / {FormatTime(_duration)}";
    }

    private static string FormatTime(TimeSpan time) =>
        time.TotalHours >= 1
            ? time.ToString(@"h\:mm\:ss\.f", CultureInfo.InvariantCulture)
            : time.ToString(@"m\:ss\.f", CultureInfo.InvariantCulture);

    // ---------- Formaat en opslaan ----------

    private void Format_Click(object sender, RoutedEventArgs e) =>
        SetFormat(ReferenceEquals(sender, GifToggle) ? ScreencastFormat.Gif : ScreencastFormat.Mp4);

    private void SetFormat(ScreencastFormat format)
    {
        Mp4Toggle.IsChecked = format == ScreencastFormat.Mp4;
        GifToggle.IsChecked = format == ScreencastFormat.Gif;
        GifOptions.Visibility = format == ScreencastFormat.Gif ? Visibility.Visible : Visibility.Collapsed;
        bool copy = CopiesOnSave;
        string ellipsis = string.IsNullOrEmpty(_settings.VideoSaveFolder) ? "..." : string.Empty;
        string action = copy ? Strings.Screencast_SaveAndCopyAsFormat : Strings.Screencast_SaveAsFormat;
        SaveText.Text = string.Format(action, format == ScreencastFormat.Gif ? "GIF" : "MP4") + ellipsis;
        SaveButton.ToolTip = copy
            ? Strings.Screencast_SaveAndCopyTip
            : Strings.Screencast_SaveButtonTip;
        UploadButton.Visibility = _settings.Uploader is { IsConfigured: true } ? Visibility.Visible : Visibility.Collapsed;
        UpdateInfo();
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        SetFormat(Format);
    }

    private void UpdateInfo()
    {
        int width = _loaded ? Player.NaturalVideoWidth : 0;
        int height = _loaded ? Player.NaturalVideoHeight : 0;
        string size = width > 0 ? $"{width} × {height} px" : string.Empty;
        if (Format == ScreencastFormat.Gif)
        {
            int gifWidth = GifWidth;
            if (width > 0 && gifWidth > 0 && gifWidth < width)
            {
                size = $"{gifWidth} × {(int)Math.Round(height * (double)gifWidth / width)} px";
            }

            string warning = (_end - _start).TotalSeconds > 30 ? "  ·  " + Strings.Screencast_LongGifWarning : string.Empty;
            InfoText.Text = $"{size}  ·  {Strings.Screencast_GifNoSound}{warning}";
        }
        else
        {
            string audio = _loaded ? (Player.HasAudio ? Strings.Screencast_WithSound : Strings.Screencast_WithoutSound) : string.Empty;
            InfoText.Text = string.Join("  ·  ", new[] { size, audio }.Where(s => s.Length > 0));
        }
    }

    private string Extension => Format == ScreencastFormat.Gif ? ".gif" : ".mp4";

    private static string DefaultName => string.Format(Strings.Screencast_DefaultName, DateTime.Now);

    private string DefaultFileName => DefaultName + Extension;

    private static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "kniip");

    private bool CopiesOnSave => _settings.SaveButton == SaveButtonChoice.SaveAndCopy;

    private async void Save_Click(object sender, RoutedEventArgs e) =>
        await SaveAsync(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), CopiesOnSave);

    private void SaveMenu_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (IsBusy || !SaveButton.IsEnabled)
        {
            return;
        }

        bool copy = CopiesOnSave;
        string format = Format == ScreencastFormat.Gif ? "GIF" : "MP4";
        string ellipsis = string.IsNullOrEmpty(_settings.VideoSaveFolder) ? "..." : string.Empty;
        var menu = new ContextMenu { PlacementTarget = SaveButton, Placement = PlacementMode.Bottom };
        menu.Items.Add(SaveMenuItem(string.Format(copy ? Strings.Screencast_SaveAsFormat : Strings.Screencast_SaveAndCopyAsFormat, format) + ellipsis, false, !copy));
        menu.Items.Add(SaveMenuItem(Strings.Common_SaveAs, true, copy));
        menu.IsOpen = true;
    }

    private MenuItem SaveMenuItem(string header, bool ask, bool copy)
    {
        var item = new MenuItem { Header = header };
        item.Click += async (_, _) => await SaveAsync(ask, copy);
        return item;
    }

    private async Task SaveAsync(bool ask, bool copy)
    {
        if (IsBusy)
        {
            return;
        }

        SetPlaying(false);
        string? target = ask ? null : FixedTarget();
        if (target is null)
        {
            target = AskTarget();
            if (target is null)
            {
                return;
            }

            _settings.LastVideoFolder = Path.GetDirectoryName(target);
        }

        if (await ExportAsync(target))
        {
            _kept = true;
            RememberChoices();
            AddToHistory(target, null, target);
            bool copied = !copy || ClipboardWriter.SetFile(target);
            App.Instance.ShowBalloon(copy && copied ? Strings.Screencast_SavedCopied : Strings.Screencast_Saved, target);
            if (!copied)
            {
                MessageBox.Show(this, Strings.Screencast_SavedCopyFailed,
                    "kniip", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            Close();
        }
    }

    private string? FixedTarget()
    {
        if (_settings.VideoSaveFolder is not { Length: > 0 } folder)
        {
            return null;
        }

        try
        {
            return FileNameTemplate.Resolve(folder, _settings.VideoFileNameTemplate, _windowName ?? Strings.Screencast_Name, DefaultName, Extension);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, string.Format(Strings.Screencast_SaveFolderUnusable, folder, ex.Message),
                "kniip", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
    }

    private string? AskTarget()
    {
        string folder = _settings.LastVideoFolder is { } last && Directory.Exists(last) ? last : DefaultFolder;
        Directory.CreateDirectory(folder);
        bool gif = Format == ScreencastFormat.Gif;
        var dialog = new SaveFileDialog
        {
            Title = Strings.Screencast_SaveDialogTitle,
            Filter = gif ? Strings.Screencast_GifFilter : Strings.Screencast_Mp4Filter,
            FileName = FileNameTemplate.SuggestedName(_settings.VideoFileNameTemplate, _windowName ?? Strings.Screencast_Name, DefaultName) + Extension,
            InitialDirectory = folder,
            AddExtension = true
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private async void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (IsBusy)
        {
            return;
        }

        SetPlaying(false);
        string folder = Path.Combine(RecordingSession.TempFolder, "Kopieën", Guid.NewGuid().ToString("N")[..8]);
        string target = Path.Combine(folder, DefaultFileName);
        if (await ExportAsync(target))
        {
            if (!ClipboardWriter.SetFile(target))
            {
                MessageBox.Show(this, Strings.Common_CopyFailedBusyLong,
                    "kniip", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _kept = true;
            RememberChoices();
            SavedRun.Text = string.Format(Strings.Screencast_OnClipboard, Path.GetFileName(target)) + "  ";
            OpenRun.Text = Strings.Common_OpenFolder;
            _lastSaved = target;
            SavedText.Visibility = Visibility.Visible;
        }
    }

    private async void Upload_Click(object sender, RoutedEventArgs e)
    {
        if (IsBusy || _settings.Uploader is not { IsConfigured: true } config)
        {
            return;
        }

        SetPlaying(false);
        string folder = Path.Combine(RecordingSession.TempFolder, "Uploads", Guid.NewGuid().ToString("N")[..8]);
        string target = Path.Combine(folder, DefaultFileName);
        if (!await ExportAsync(target))
        {
            return;
        }

        _export = new CancellationTokenSource();
        SetBusy(true);
        var progress = new Progress<double>(value => Progress.Value = value * 100);
        try
        {
            string url;
            await using (FileStream stream = File.OpenRead(target))
            {
                string contentType = Format == ScreencastFormat.Gif ? "image/gif" : "video/mp4";
                url = await ZiplineUploader.UploadAsync(config, stream, Path.GetFileName(target), contentType, progress, _export.Token);
            }

            _kept = true;
            RememberChoices();
            AddToHistory(null, url, target);
            bool copied = ClipboardWriter.SetText(url);
            SavedRun.Text = string.Format(copied ? Strings.Screencast_UploadedCopied : Strings.Screencast_Uploaded, url) + "  ";
            OpenRun.Text = Strings.History_OpenLink;
            _lastSaved = url;
            SavedText.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, string.Format(Strings.Common_UploadFailed, ex.Message), "kniip", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _export.Dispose();
            _export = null;
            SetBusy(false);
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception)
            {
                // Blijft in de tijdelijke map staan; die wordt bij de volgende start opgeruimd.
            }
        }
    }

    /// <param name="exported">Het geëxporteerde bestand, voor de miniatuur van een GIF.</param>
    private void AddToHistory(string? file, string? url, string exported)
    {
        bool gif = Format == ScreencastFormat.Gif;
        Kniip.History.HistoryStore.Add(gif ? Kniip.History.HistoryKind.Gif : Kniip.History.HistoryKind.Mp4, file, url,
            gif ? Kniip.History.HistoryStore.PreviewOf(exported) : null);
    }

    private void RememberChoices()
    {
        _settings.LastScreencastFormat = Format;
        _settings.GifFps = GifFps;
        _settings.GifMaxWidth = GifWidth;
        SettingsStore.Save(_settings);
    }

    private async Task<bool> ExportAsync(string target)
    {
        _export = new CancellationTokenSource();
        SetBusy(true);
        var progress = new Progress<double>(value => Progress.Value = value * 100);
        try
        {
            if (Format == ScreencastFormat.Gif)
            {
                await GifExporter.ExportAsync(_path, target, _start, _end, GifFps, GifWidth, progress, _export.Token);
            }
            else
            {
                await VideoExporter.TrimMp4Async(_path, target, _start, _end, _duration, progress, _export.Token);
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, string.Format(Strings.Screencast_SaveFailed, ex.Message), "kniip", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        finally
        {
            _export.Dispose();
            _export = null;
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        BusyPanel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        Progress.Value = 0;
        SaveButton.IsEnabled = !busy;
        CopyButton.IsEnabled = !busy;
        UploadButton.IsEnabled = !busy;
        Mp4Toggle.IsEnabled = !busy;
        GifToggle.IsEnabled = !busy;
        GifOptions.IsEnabled = !busy;
        Cursor = busy ? Cursors.AppStarting : null;
    }

    private void CancelExport_Click(object sender, RoutedEventArgs e) => _export?.Cancel();

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_lastSaved == null)
        {
            return;
        }

        try
        {
            ProcessStartInfo info = Uri.TryCreate(_lastSaved, UriKind.Absolute, out Uri? uri) && uri.Scheme.StartsWith("http", StringComparison.Ordinal)
                ? new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }
                : new ProcessStartInfo("explorer.exe", $"/select,\"{_lastSaved}\"") { UseShellExecute = true };
            Process.Start(info);
        }
        catch (Exception)
        {
            // Verkenner openen is een extraatje.
        }
    }

    // ---------- Sluiten ----------

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (IsBusy)
        {
            _export?.Cancel();
            e.Cancel = true;
            return;
        }

        if (!_kept && MessageBox.Show(this, Strings.Screencast_DiscardConfirm, "kniip",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            e.Cancel = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _playTimer.Stop();
        Player.Close();
        DeleteLater(_path, attempts: 5);
    }

    /// <summary>De mediaspeler laat het bestand soms nog even vast; daarom een paar keer proberen.</summary>
    private static void DeleteLater(string path, int attempts)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException) when (attempts > 1)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                DeleteLater(path, attempts - 1);
            };
            timer.Start();
        }
        catch (Exception)
        {
            // Blijft in de tijdelijke map staan; die wordt bij de volgende start opgeruimd.
        }
    }
}
