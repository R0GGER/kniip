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
using Kniip.Watermark;
using Kniip.Workflows;
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
    private readonly WindowInfo? _window;
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
    private WorkflowContinuation? _workflow;

    /// <param name="window">Programmanaam en titel van het opgenomen venster, voor %pn en %t in het naamsjabloon.</param>
    internal ScreencastWindow(string path, AppSettings settings, ScreencastFormat? format = null, WindowInfo? window = null)
    {
        InitializeComponent();
        Infrastructure.ThemeManager.Attach(this);
        _path = path;
        _settings = settings;
        _window = window;

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
        UpdateWatermarkPreview();
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
        UploadButton.Visibility = _settings.DefaultUploader != null ? Visibility.Visible : Visibility.Collapsed;
        UploadMenuChevron.Visibility = _settings.Uploaders.Count(u => u.IsConfigured) > 1 ? Visibility.Visible : Visibility.Collapsed;
        UpdateInfo();
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        SetFormat(Format);
        UpdateWatermarkPreview();
    }

    // ---------- Watermerk ----------

    /// <summary>Het watermerk uit de instellingen, als de knop Watermerk aan staat.</summary>
    private WatermarkSettings? Watermark => WatermarkToggle.IsChecked == true ? _settings.Watermark : null;

    private void Watermark_Click(object sender, RoutedEventArgs e)
    {
        if (WatermarkToggle.IsChecked == true && !WatermarkRenderer.IsUsable(_settings.Watermark))
        {
            WatermarkToggle.IsChecked = false;
            if (MessageBox.Show(this, Strings.Watermark_NotSet, "kniip", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            {
                App.Instance.ShowWatermarkSettings();
            }
        }

        UpdateWatermarkPreview();
    }

    /// <summary>Toont het watermerk over de video zoals het in het bestand komt; na een wijziging in de instellingen ook bijgewerkt.</summary>
    private void UpdateWatermarkPreview()
    {
        int width = _loaded ? Player.NaturalVideoWidth : 0;
        int height = _loaded ? Player.NaturalVideoHeight : 0;
        if (Watermark is not { } watermark || width <= 0 || height <= 0 || !WatermarkRenderer.IsUsable(watermark))
        {
            WatermarkOverlay.Source = null;
            WatermarkOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        // Grootte en marge zijn percentages; een kleiner voorbeeld ziet er dus hetzelfde uit.
        double scale = Math.Min(1, 1280.0 / width);
        WatermarkOverlay.Source = WatermarkRenderer.RenderOverlayBitmap((int)Math.Round(width * scale), (int)Math.Round(height * scale), watermark);
        WatermarkOverlay.Visibility = Visibility.Visible;
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

    private string Extension => WorkflowActions.VideoExtension(Format);

    private static string DefaultName => WorkflowActions.VideoDefaultName;

    private string DefaultFileName => DefaultName + Extension;

    private WindowInfo TemplateWindow => WorkflowActions.VideoTemplateWindow(_window);

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

            if (_workflow is { IsDone: false, NextIsSave: true } workflow)
            {
                EndWorkflow();
                workflow.Continue(new WorkflowEditResult(null, target, Format, target));
            }

            Close();
        }
    }

    private string? FixedTarget()
    {
        try
        {
            return WorkflowActions.VideoTarget(_settings, _window, Format);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, string.Format(Strings.Screencast_SaveFolderUnusable, _settings.VideoSaveFolder, ex.Message),
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
            FileName = FileNameTemplate.SuggestedName(_settings.VideoFileNameTemplate, TemplateWindow, DefaultName) + Extension,
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

    private async void Upload_Click(object sender, RoutedEventArgs e) => await UploadAsync(_settings.UploaderFor(UploadKind.Media));

    private void UploadMenu_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (IsBusy || !UploadButton.IsEnabled)
        {
            return;
        }

        var menu = new ContextMenu { PlacementTarget = UploadButton, Placement = PlacementMode.Bottom };
        foreach (UploadDestination destination in _settings.Uploaders.Where(u => u.IsConfigured))
        {
            UploadDestination target = destination;
            var item = new MenuItem { Header = string.Format(Strings.Common_UploadToNamed, target.Title) };
            item.Click += async (_, _) => await UploadAsync(target);
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    private async Task UploadAsync(UploadDestination? config)
    {
        if (IsBusy || config is not { IsConfigured: true })
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
            string url = await WorkflowActions.UploadVideoAsync(config, target, Format, progress, _export.Token);
            _kept = true;
            RememberChoices();
            bool copied = ClipboardWriter.SetText(url);
            SavedRun.Text = string.Format(copied ? Strings.Screencast_UploadedCopied : Strings.Screencast_Uploaded, url) + "  ";
            OpenRun.Text = Strings.History_OpenLink;
            _lastSaved = url;
            SavedText.Visibility = Visibility.Visible;
            App.Instance.ShowLinkBalloon(copied ? Strings.Upload_DoneCopiedTitle : Strings.Upload_QuickDoneTitle, url);
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
    private void AddToHistory(string? file, string? url, string exported) =>
        WorkflowActions.AddVideoHistory(Format, file, url, exported);

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
                await GifExporter.ExportAsync(_path, target, _start, _end, GifFps, GifWidth, progress, _export.Token, Watermark);
            }
            else
            {
                await VideoExporter.TrimMp4Async(_path, target, _start, _end, _duration, progress, _export.Token, Watermark);
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
        WatermarkToggle.IsEnabled = !busy;
        WorkflowContinueButton.IsEnabled = !busy;
        WorkflowStopButton.IsEnabled = !busy;
        Cursor = busy ? Cursors.AppStarting : null;
    }

    // ---------- Workflow ----------

    /// <summary>Dit venster is de stap Bewerken van een workflow; de balk onderaan zet hem voort.</summary>
    internal void AttachWorkflow(WorkflowContinuation workflow)
    {
        _workflow = workflow;
        WorkflowTitle.Text = string.Format(Strings.Workflow_BannerTitle, workflow.WorkflowName);
        WorkflowNext.Text = string.Format(Strings.Workflow_BannerNext, workflow.RemainingSteps)
                            + (workflow.NextIsSave ? "  ·  " + Strings.Workflow_BannerSaveContinues : string.Empty);
        WorkflowBanner.Visibility = Visibility.Visible;
        if (workflow.Watermark && WatermarkRenderer.IsUsable(_settings.Watermark))
        {
            WatermarkToggle.IsChecked = true;
            UpdateWatermarkPreview();
        }
    }

    private void EndWorkflow()
    {
        _workflow = null;
        WorkflowBanner.Visibility = Visibility.Collapsed;
    }

    /// <summary>Exporteert met het gekozen formaat en de inkorting naar een tijdelijk bestand en geeft dat aan de workflow.</summary>
    private async void WorkflowContinue_Click(object sender, RoutedEventArgs e)
    {
        if (IsBusy || _workflow is null)
        {
            return;
        }

        SetPlaying(false);
        string target = Path.Combine(WorkflowActions.NewTempFolder("Workflows"), DefaultFileName);
        if (!await ExportAsync(target) || _workflow is not { } workflow)
        {
            return;
        }

        _kept = true;
        RememberChoices();
        EndWorkflow();
        workflow.Continue(new WorkflowEditResult(null, target, Format, null));
        Close();
    }

    private void WorkflowStop_Click(object sender, RoutedEventArgs e)
    {
        WorkflowContinuation? workflow = _workflow;
        EndWorkflow();
        workflow?.Cancel();
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
        WorkflowContinuation? workflow = _workflow;
        _workflow = null;
        workflow?.Cancel();
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
