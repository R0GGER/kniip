using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kniip.Capture;
using Kniip.Editor;
using Kniip.Infrastructure;
using Kniip.Update;
using Kniip.Upload;

namespace Kniip.Settings;

public partial class SettingsWindow : Window
{
    private const string HotkeyHintText =
        "kniip vangt deze toetsen in alle programma's af; bij Print Screen opent het Windows-knipprogramma dan niet óók.";

    private static readonly Dictionary<string, string> HotkeyNames = new()
    {
        ["Snip"] = "rechthoek knippen",
        ["Window"] = "venster knippen",
        ["Record"] = "screencast (MP4)",
        ["Gif"] = "screencast (GIF)",
        ["Clipboard"] = "bewerken vanaf klembord",
        ["Ocr"] = "OCR",
    };

    private readonly AppSettings _settings;
    private readonly Dictionary<string, string> _hotkeys;
    private readonly Dictionary<string, Button> _hotkeyButtons;
    private string? _listeningFor;
    private string? _snipFolder;
    private string? _videoFolder;
    private UploaderConfig? _uploader;
    private bool _resetEditor;
    private bool _updateChecked;
    private ReleaseInfo? _update;
    private System.Threading.CancellationTokenSource? _updateCts;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        ThemeManager.Attach(this);
        _settings = settings;
        Title = $"{AppIdentity.DisplayName} instellingen";

        FolderText.Text = CaptureCoordinator.ScreenshotsFolder ?? "Map Schermafbeeldingen niet gevonden.";
        AutostartBox.IsChecked = ShellIntegration.IsAutostartEnabled();
        OpenWithBox.IsChecked = ShellIntegration.IsOpenWithRegistered();

        WatchClipboardBox.Checked += (_, _) => OnlySnippingToolBox.IsEnabled = true;
        WatchClipboardBox.Unchecked += (_, _) => OnlySnippingToolBox.IsEnabled = false;

        _uploader = settings.Uploader;
        ShowUploader();

        _hotkeys = new Dictionary<string, string>();
        _hotkeyButtons = new Dictionary<string, Button>
        {
            ["Snip"] = SnipHotkeyButton,
            ["Window"] = WindowHotkeyButton,
            ["Record"] = RecordHotkeyButton,
            ["Gif"] = GifHotkeyButton,
            ["Clipboard"] = ClipboardHotkeyButton,
            ["Ocr"] = OcrHotkeyButton,
        };

        HotkeyHint.Text = App.Instance.HotkeyHook?.IsActive == true
            ? HotkeyHintText
            : "De toetsenbordhaak kon niet worden gestart; de sneltoetsen werken nu niet.";
        Deactivated += (_, _) => StopListening();
        App.Instance.Updates.AvailabilityChanged += OnUpdateAvailabilityChanged;
        Closed += (_, _) =>
        {
            StopListening();
            _updateCts?.Cancel();
            App.Instance.Updates.AvailabilityChanged -= OnUpdateAvailabilityChanged;
        };
        ShowUpdateBadge();

        CurrentVersionText.Text = $"Geïnstalleerde versie: {UpdateChecker.CurrentVersion}";
        LoadFrom(settings);

        MaxHeight = SystemParameters.WorkArea.Height;
    }

    /// <summary>Vult het formulier; de uploader en de Windows-integratie blijven ongemoeid.</summary>
    private void LoadFrom(AppSettings settings)
    {
        WatchClipboardBox.IsChecked = settings.WatchClipboard;
        OnlySnippingToolBox.IsChecked = settings.OnlyFromSnippingTool;
        OnlySnippingToolBox.IsEnabled = settings.WatchClipboard;
        WatchFolderBox.IsChecked = settings.WatchScreenshotsFolder;
        ActionNotifyRadio.IsChecked = settings.Action == CaptureAction.Notify;
        ActionOpenRadio.IsChecked = settings.Action == CaptureAction.OpenEditor;
        ActionNothingRadio.IsChecked = settings.Action == CaptureAction.Nothing;
        StraightHighlightBox.IsChecked = settings.StraightHighlight;
        ColorModeLightRadio.IsChecked = settings.ColorMode == ColorMode.Light;
        ColorModeDarkRadio.IsChecked = settings.ColorMode == ColorMode.Dark;
        ColorModeSystemRadio.IsChecked = settings.ColorMode == ColorMode.System;
        LayoutSplitRadio.IsChecked = settings.EditorLayout == EditorLayout.Split;
        LayoutThreeSplitRadio.IsChecked = settings.EditorLayout == EditorLayout.ThreeSplit;
        LayoutSingleRadio.IsChecked = settings.EditorLayout == EditorLayout.Single;
        CloseAskRadio.IsChecked = settings.EditorCloseBehavior == EditorCloseBehavior.Ask;
        CloseAllRadio.IsChecked = settings.EditorCloseBehavior == EditorCloseBehavior.CloseAll;
        CloseKeepRadio.IsChecked = settings.EditorCloseBehavior == EditorCloseBehavior.KeepTabs;
        bool docked = !settings.CaptureBarFloating;
        CaptureBarLeftRadio.IsChecked = docked && settings.CaptureBarPosition == Dock.Left;
        CaptureBarRightRadio.IsChecked = docked && settings.CaptureBarPosition == Dock.Right;
        CaptureBarTopRadio.IsChecked = docked && settings.CaptureBarPosition == Dock.Top;
        CaptureBarBottomRadio.IsChecked = docked && settings.CaptureBarPosition == Dock.Bottom;
        CaptureBarFloatRadio.IsChecked = settings.CaptureBarFloating;
        AutoUpdateCheckBox.IsChecked = settings.CheckForUpdates;
        MagnifierBox.IsChecked = settings.ShowMagnifier;

        _snipFolder = settings.SnipSaveFolder;
        _videoFolder = settings.VideoSaveFolder;
        ShowSaveFolders();
        SnipTemplateBox.Text = settings.SnipFileNameTemplate ?? string.Empty;
        VideoTemplateBox.Text = settings.VideoFileNameTemplate ?? string.Empty;
        ShowTemplatePreviews();
        SaveAndCopyRadio.IsChecked = settings.SaveButton == SaveButtonChoice.SaveAndCopy;
        SaveOnlyRadio.IsChecked = settings.SaveButton != SaveButtonChoice.SaveAndCopy;

        _hotkeys["Snip"] = settings.SnipHotkey;
        _hotkeys["Window"] = settings.WindowHotkey;
        _hotkeys["Record"] = settings.RecordHotkey;
        _hotkeys["Gif"] = settings.GifHotkey;
        _hotkeys["Clipboard"] = settings.ClipboardHotkey;
        _hotkeys["Ocr"] = settings.OcrHotkey;
        foreach (string id in _hotkeys.Keys)
        {
            ShowHotkey(id);
        }

        SystemAudioBox.IsChecked = settings.RecordSystemAudio;
        MicrophoneBox.IsChecked = settings.RecordMicrophone;
        ShowCursorBox.IsChecked = settings.RecordShowCursor;
        HighlightClicksBox.IsChecked = settings.RecordHighlightClicks;
        FillChoices(VideoFpsCombo, new[] { 15, 30, 60 }, settings.RecordFps, fps => $"{fps} beelden/s");
        FillChoices(GifFpsCombo, new[] { 5, 10, 15, 20, 25 }, settings.GifFps, fps => $"{fps} beelden/s");
        FillChoices(GifWidthCombo, new[] { 0, 1920, 1280, 960, 800, 640, 480 }, settings.GifMaxWidth,
            width => width == 0 ? "Originele breedte" : $"max. {width} px breed");
    }

    private void ResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        StopListening();
        LoadFrom(new AppSettings());
        _resetEditor = true;
        ResetStatus.Visibility = Visibility.Visible;
    }

    private static void FillChoices(ComboBox combo, int[] values, int current, Func<int, string> label)
    {
        combo.Items.Clear();
        foreach (int value in values.Contains(current) ? values : values.Append(current).ToArray())
        {
            var item = new ComboBoxItem { Content = label(value), Tag = value };
            combo.Items.Add(item);
            if (value == current)
            {
                combo.SelectedItem = item;
            }
        }
    }

    private static int SelectedValue(ComboBox combo, int fallback) =>
        combo.SelectedItem is ComboBoxItem { Tag: int value } ? value : fallback;

    private void ShowHotkey(string id) =>
        _hotkeyButtons[id].Content = HotkeyGesture.TryParse(_hotkeys[id], out HotkeyGesture gesture) ? gesture.DisplayText : "(geen)";

    private void HotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        GlobalHotkeyHook? hook = App.Instance.HotkeyHook;
        if (hook is not { IsActive: true } || sender is not FrameworkElement { Tag: string id })
        {
            return;
        }

        StopListening();
        // Via de haak, niet via WPF: Print Screen komt nooit als KeyDown bij een venster aan.
        _listeningFor = id;
        hook.Capture = OnHotkeyPressed;
        _hotkeyButtons[id].Content = "Druk op een toets...";
        HotkeyHint.Text = "Esc = annuleren  ·  Delete = geen sneltoets";
    }

    private void OnHotkeyPressed(HotkeyGesture pressed)
    {
        if (_listeningFor is not string id)
        {
            return;
        }

        if (pressed.Modifiers == ModifierKeys.None && pressed.Key == Key.Escape)
        {
            StopListening();
            return;
        }

        if (pressed.Modifiers == ModifierKeys.None && pressed.Key is Key.Delete or Key.Back)
        {
            _hotkeys[id] = string.Empty;
            StopListening();
            return;
        }

        if (!pressed.IsAllowed)
        {
            HotkeyHint.Text = $"{pressed.DisplayText} gebruik je om te typen. Kies Print Screen, Pause, een F-toets of een combinatie met Ctrl, Alt of Win.";
            return;
        }

        foreach ((string other, string hotkey) in _hotkeys)
        {
            if (other != id && HotkeyGesture.TryParse(hotkey, out HotkeyGesture taken) && taken == pressed)
            {
                HotkeyHint.Text = $"{pressed.DisplayText} is al de sneltoets voor {HotkeyNames[other]}; kies een andere.";
                return;
            }
        }

        _hotkeys[id] = pressed.ToString();
        StopListening();
    }

    private void StopListening()
    {
        if (_listeningFor is not string id)
        {
            return;
        }

        _listeningFor = null;
        if (App.Instance.HotkeyHook is { } hook)
        {
            hook.Capture = null;
        }

        ShowHotkey(id);
        HotkeyHint.Text = HotkeyHintText;
    }

    private void ClearHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id })
        {
            return;
        }

        StopListening();
        _hotkeys[id] = string.Empty;
        ShowHotkey(id);
    }

    private void ShowSaveFolders()
    {
        ShowSaveFolder(SnipFolderText, _snipFolder);
        ShowSaveFolder(VideoFolderText, _videoFolder);
    }

    private static void ShowSaveFolder(TextBlock text, string? folder)
    {
        text.Text = string.IsNullOrEmpty(folder) ? "Elke keer vragen" : folder;
        text.ToolTip = folder;
        text.SetResourceReference(TextBlock.ForegroundProperty, string.IsNullOrEmpty(folder) ? "SubtleTextBrush" : "InkBrush");
    }

    private void Template_TextChanged(object sender, TextChangedEventArgs e) => ShowTemplatePreviews();

    private void ShowTemplatePreviews()
    {
        TemplateHelp.Text = FileNameTemplate.Help;
        SnipTemplatePreview.Text = "Voorbeeld: " + FileNameTemplate.Preview(NullIfBlank(SnipTemplateBox.Text), "Cursor",
            $"Knipsel {DateTime.Now:yyyy-MM-dd HHmmss}", ".png");
        VideoTemplatePreview.Text = "Voorbeeld: " + FileNameTemplate.Preview(NullIfBlank(VideoTemplateBox.Text), "Cursor",
            $"Screencast {DateTime.Now:yyyy-MM-dd HHmmss}", ".mp4");
    }

    private static string? NullIfBlank(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private void BrowseSaveFolder_Click(object sender, RoutedEventArgs e)
    {
        bool snip = sender is FrameworkElement { Tag: "Snip" };
        string? current = snip ? _snipFolder : _videoFolder;
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = snip ? "Map voor knipsels kiezen" : "Map voor screencasts kiezen",
            InitialDirectory = current is { Length: > 0 } && System.IO.Directory.Exists(current)
                ? current
                : Environment.GetFolderPath(snip ? Environment.SpecialFolder.MyPictures : Environment.SpecialFolder.MyVideos)
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (snip)
        {
            _snipFolder = dialog.FolderName;
        }
        else
        {
            _videoFolder = dialog.FolderName;
        }

        ShowSaveFolders();
    }

    private void ClearSaveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: "Snip" })
        {
            _snipFolder = null;
        }
        else
        {
            _videoFolder = null;
        }

        ShowSaveFolders();
    }

    private void ShowUploader()
    {
        bool configured = _uploader is { IsConfigured: true };
        UploaderText.Text = configured ? _uploader!.DisplayName : "Niet ingesteld";
        UploaderText.ToolTip = configured ? _uploader!.RequestUrl : null;
        UploaderText.SetResourceReference(TextBlock.ForegroundProperty, configured ? "InkBrush" : "SubtleTextBrush");
        TestUploaderButton.IsEnabled = configured;
        RemoveUploaderButton.IsEnabled = configured;
    }

    private void ShowUploaderStatus(string? message, string? url = null, string? suffix = null)
    {
        UploaderStatus.Inlines.Clear();
        UploaderStatus.Inlines.Add(new Run(message ?? string.Empty));
        if (url != null)
        {
            var link = new Hyperlink(new Run(url)) { ToolTip = "Openen in de browser" };
            link.Click += (_, _) => OpenUrl(url);
            UploaderStatus.Inlines.Add(link);
            UploaderStatus.Inlines.Add(new Run(suffix ?? string.Empty));
        }

        UploaderStatus.Visibility = string.IsNullOrEmpty(message) && url == null ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // De link staat ook op het klembord.
        }
    }

    private void ImportUploader_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Zipline-uploader importeren",
            Filter = "Zipline-uploader (*.sxcu;*.sh)|*.sxcu;*.sh|ShareX-uploader (*.sxcu)|*.sxcu|Shell script (*.sh)|*.sh|Alle bestanden (*.*)|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) is { } home
                               && System.IO.Directory.Exists(System.IO.Path.Combine(home, "Downloads"))
                ? System.IO.Path.Combine(home, "Downloads")
                : string.Empty
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            _uploader = UploaderImporter.FromFile(dialog.FileName);
            ShowUploader();
            ShowUploaderStatus($"Geïmporteerd: {_uploader.Name}. Klik op Opslaan om dit te bewaren.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Dit bestand kan niet worden gebruikt:\n{dialog.FileName}\n\n{ex.Message}", "kniip",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void TestUploader_Click(object sender, RoutedEventArgs e)
    {
        if (_uploader is not { IsConfigured: true } config)
        {
            return;
        }

        TestUploaderButton.IsEnabled = false;
        ShowUploaderStatus("Testplaatje uploaden...");
        try
        {
            using System.IO.MemoryStream png = ImageExporter.EncodePng(CreateTestImage());
            string url = await ZiplineUploader.UploadAsync(config, png, "kniip-test.png", "image/png");
            bool copied = ClipboardWriter.SetText(url);
            ShowUploaderStatus("Gelukt: ", url, copied ? " (gekopieerd)" : null);
        }
        catch (Exception ex)
        {
            ShowUploaderStatus(null);
            MessageBox.Show(this, $"De test-upload is mislukt.\n\n{ex.Message}", "kniip", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            TestUploaderButton.IsEnabled = _uploader is { IsConfigured: true };
        }
    }

    private static BitmapSource CreateTestImage()
    {
        const int size = 64;
        Color accent = Application.Current.Resources["AccentBrush"] is SolidColorBrush brush ? brush.Color : Colors.SteelBlue;
        var pixels = new byte[size * size * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = accent.B;
            pixels[i + 1] = accent.G;
            pixels[i + 2] = accent.R;
            pixels[i + 3] = 255;
        }

        return BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
    }

    private void RemoveUploader_Click(object sender, RoutedEventArgs e)
    {
        _uploader = null;
        ShowUploader();
        ShowUploaderStatus(null);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.Uploader = _uploader;
        _settings.SnipSaveFolder = _snipFolder;
        _settings.VideoSaveFolder = _videoFolder;
        _settings.SnipFileNameTemplate = NullIfBlank(SnipTemplateBox.Text);
        _settings.VideoFileNameTemplate = NullIfBlank(VideoTemplateBox.Text);
        _settings.SaveButton = SaveAndCopyRadio.IsChecked == true
            ? SaveButtonChoice.SaveAndCopy
            : SaveButtonChoice.SaveOnly;
        _settings.WatchClipboard = WatchClipboardBox.IsChecked == true;
        _settings.OnlyFromSnippingTool = OnlySnippingToolBox.IsChecked == true;
        _settings.WatchScreenshotsFolder = WatchFolderBox.IsChecked == true;
        _settings.Action = ActionOpenRadio.IsChecked == true ? CaptureAction.OpenEditor
            : ActionNothingRadio.IsChecked == true ? CaptureAction.Nothing
            : CaptureAction.Notify;
        _settings.StraightHighlight = StraightHighlightBox.IsChecked == true;
        _settings.ColorMode = ColorModeDarkRadio.IsChecked == true ? ColorMode.Dark
            : ColorModeSystemRadio.IsChecked == true ? ColorMode.System
            : ColorMode.Light;
        _settings.EditorLayout = LayoutSplitRadio.IsChecked == true ? EditorLayout.Split
            : LayoutThreeSplitRadio.IsChecked == true ? EditorLayout.ThreeSplit
            : EditorLayout.Single;
        _settings.EditorCloseBehavior = CloseAllRadio.IsChecked == true ? EditorCloseBehavior.CloseAll
            : CloseKeepRadio.IsChecked == true ? EditorCloseBehavior.KeepTabs
            : EditorCloseBehavior.Ask;
        _settings.CaptureBarFloating = CaptureBarFloatRadio.IsChecked == true;
        if (!_settings.CaptureBarFloating)
        {
            _settings.CaptureBarPosition = CaptureBarRightRadio.IsChecked == true ? Dock.Right
                : CaptureBarTopRadio.IsChecked == true ? Dock.Top
                : CaptureBarBottomRadio.IsChecked == true ? Dock.Bottom
                : Dock.Left;
        }
        _settings.CheckForUpdates = AutoUpdateCheckBox.IsChecked == true;
        _settings.ShowMagnifier = MagnifierBox.IsChecked == true;
        StopListening();
        _settings.SnipHotkey = _hotkeys["Snip"];
        _settings.WindowHotkey = _hotkeys["Window"];
        _settings.RecordHotkey = _hotkeys["Record"];
        _settings.GifHotkey = _hotkeys["Gif"];
        _settings.ClipboardHotkey = _hotkeys["Clipboard"];
        _settings.OcrHotkey = _hotkeys["Ocr"];
        _settings.RecordSystemAudio = SystemAudioBox.IsChecked == true;
        _settings.RecordMicrophone = MicrophoneBox.IsChecked == true;
        _settings.RecordShowCursor = ShowCursorBox.IsChecked == true;
        _settings.RecordHighlightClicks = HighlightClicksBox.IsChecked == true;
        _settings.RecordFps = SelectedValue(VideoFpsCombo, 30);
        _settings.GifFps = SelectedValue(GifFpsCombo, 10);
        _settings.GifMaxWidth = SelectedValue(GifWidthCombo, 800);

        if (_resetEditor)
        {
            var defaults = new AppSettings();
            _settings.ToolStyles = defaults.ToolStyles;
            _settings.LastTool = defaults.LastTool;
            _settings.LastSaveFolder = defaults.LastSaveFolder;
            _settings.LastVideoFolder = defaults.LastVideoFolder;
            _settings.ToolbarPosition = defaults.ToolbarPosition;
            _settings.ToolbarLocked = defaults.ToolbarLocked;
            _settings.ToolbarOrder = defaults.ToolbarOrder;
            _settings.SplitToolbarOrder = defaults.SplitToolbarOrder;
            _settings.ToolbarHidden = defaults.ToolbarHidden;
        }

        try
        {
            ShellIntegration.SetAutostart(AutostartBox.IsChecked == true);

            bool openWith = OpenWithBox.IsChecked == true;
            if (openWith && !ShellIntegration.IsOpenWithRegistered())
            {
                ShellIntegration.RegisterOpenWith();
            }
            else if (!openWith && ShellIntegration.IsOpenWithRegistered())
            {
                ShellIntegration.UnregisterOpenWith();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Windows-integratie kon niet worden bijgewerkt:\n\n{ex.Message}", "kniip",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        App.Instance.SaveSettings();
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // ComboBox-keuzes bubbelen dezelfde gebeurtenis; alleen een echt tabblad telt.
        if (e.OriginalSource is TabControl)
        {
            StopListening();
            if (UpdateTab.IsSelected && !_updateChecked)
            {
                _ = CheckForUpdatesAsync();
            }
        }
    }

    private void CheckUpdate_Click(object sender, RoutedEventArgs e) => _ = CheckForUpdatesAsync();

    private void OnUpdateAvailabilityChanged(object? sender, EventArgs e) => ShowUpdateBadge();

    private void ShowUpdateBadge() =>
        UpdateTabBadge.Visibility = App.Instance.Updates.IsUpdateAvailable ? Visibility.Visible : Visibility.Collapsed;

    private void ReleasePage_Click(object sender, RoutedEventArgs e) => OpenUrl(UpdateChecker.ReleasesPage);

    private async System.Threading.Tasks.Task CheckForUpdatesAsync()
    {
        _updateChecked = true;
        _updateCts?.Cancel();
        var cts = new System.Threading.CancellationTokenSource();
        _updateCts = cts;

        _update = null;
        UpdateButton.Visibility = Visibility.Collapsed;
        CheckUpdateButton.IsEnabled = false;
        UpdateStatus.Text = "Controleren op updates...";
        try
        {
            IReadOnlyList<ReleaseInfo> releases = await UpdateChecker.GetReleasesAsync(cts.Token);
            ShowReleases(releases);
        }
        catch (UpdateException ex)
        {
            UpdateStatus.Text = ex.Message;
            ShowChangelog(Array.Empty<ReleaseInfo>(), "Geen changelog beschikbaar.");
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (_updateCts == cts)
            {
                _updateCts = null;
                CheckUpdateButton.IsEnabled = true;
            }

            cts.Dispose();
        }
    }

    private void ShowReleases(IReadOnlyList<ReleaseInfo> releases)
    {
        Version current = UpdateChecker.CurrentVersion;
        List<ReleaseInfo> newer = releases.Where(r => r.Version > current).ToList();
        App.Instance.Updates.Report(newer.FirstOrDefault());
        if (newer.Count == 0)
        {
            UpdateStatus.Text = releases.Count == 0
                ? "Er zijn nog geen releases gepubliceerd."
                : $"Je gebruikt de nieuwste versie ({current}).";
            ReleaseInfo? notes = releases.FirstOrDefault(r => r.Version == current) ?? releases.FirstOrDefault();
            ShowChangelog(notes == null ? Array.Empty<ReleaseInfo>() : new[] { notes }, "Geen changelog beschikbaar.");
            return;
        }

        if (AppIdentity.IsTest)
        {
            UpdateStatus.Text = $"Versie {newer[0].Version} is beschikbaar. Deze testversie werkt zichzelf niet bij; " +
                                "de installer zou de gewone kniip overschrijven.";
            ShowChangelog(newer, null);
            return;
        }

        _update = newer.FirstOrDefault(r => r.InstallerUrl != null);
        if (_update != null)
        {
            UpdateStatus.Text = $"Versie {_update.Version} is beschikbaar.";
            UpdateButton.Content = $"Updaten naar {_update.Version}";
            UpdateButton.Visibility = Visibility.Visible;
        }
        else
        {
            UpdateStatus.Text = $"Versie {newer[0].Version} is beschikbaar, maar zonder installer. Download die via de releasepagina.";
        }

        ShowChangelog(newer, null);
    }

    private void ShowChangelog(IReadOnlyList<ReleaseInfo> releases, string? emptyText)
    {
        FlowDocument document = MarkdownRenderer.CreateDocument();
        var culture = System.Globalization.CultureInfo.GetCultureInfo("nl-NL");
        var baseUri = new Uri(UpdateChecker.RepositoryUrl);
        foreach (ReleaseInfo release in releases)
        {
            Paragraph heading = MarkdownRenderer.Heading($"Versie {release.Version}", 17,
                new Thickness(0, document.Blocks.Count == 0 ? 0 : 14, 0, 0));
            document.Blocks.Add(heading);
            if (release.Date is DateTimeOffset date)
            {
                document.Blocks.Add(new Paragraph(new Run(date.ToLocalTime().ToString("d MMMM yyyy", culture)))
                {
                    Foreground = (Brush)Application.Current.Resources["SubtleTextBrush"],
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 0, 8),
                });
            }

            if (string.IsNullOrWhiteSpace(release.Body))
            {
                document.Blocks.Add(new Paragraph(new Run("Geen beschrijving."))
                {
                    Foreground = (Brush)Application.Current.Resources["SubtleTextBrush"],
                });
            }
            else
            {
                MarkdownRenderer.Append(document, release.Body, baseUri);
            }
        }

        if (releases.Count == 0 && emptyText != null)
        {
            document.Blocks.Add(new Paragraph(new Run(emptyText))
            {
                Foreground = (Brush)Application.Current.Resources["SubtleTextBrush"],
            });
        }

        ChangelogViewer.Document = document;
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (_update is not { } release)
        {
            return;
        }

        string installFolder = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Kniip");
        bool installed = Environment.ProcessPath is { } exe
                         && string.Equals(System.IO.Path.GetDirectoryName(exe), installFolder, StringComparison.OrdinalIgnoreCase);
        string message = $"kniip wordt afgesloten, bijgewerkt naar versie {release.Version} en daarna opnieuw gestart. " +
                         "Open editors en screencasts worden eerst gesloten.";
        if (!installed)
        {
            message += $"\n\nDeze kniip is niet via de installer gestart; de nieuwe versie wordt geïnstalleerd in {installFolder}.";
        }

        if (MessageBox.Show(this, message, "kniip bijwerken", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK)
        {
            return;
        }

        var cts = new System.Threading.CancellationTokenSource();
        _updateCts = cts;
        UpdateButton.IsEnabled = false;
        CheckUpdateButton.IsEnabled = false;
        DownloadProgress.Value = 0;
        DownloadProgress.Visibility = Visibility.Visible;
        UpdateStatus.Text = $"Versie {release.Version} downloaden...";
        string path;
        try
        {
            path = await UpdateInstaller.DownloadAsync(release, new Progress<double>(p => DownloadProgress.Value = p), cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex) when (ex is UpdateException or System.IO.IOException or UnauthorizedAccessException)
        {
            UpdateStatus.Text = $"Versie {release.Version} is beschikbaar.";
            MessageBox.Show(this, $"De update kon niet worden gedownload.\n\n{ex.Message}", "kniip",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        finally
        {
            if (_updateCts == cts)
            {
                _updateCts = null;
            }

            cts.Dispose();
            if (IsLoaded)
            {
                DownloadProgress.Visibility = Visibility.Collapsed;
                UpdateButton.IsEnabled = true;
                CheckUpdateButton.IsEnabled = true;
            }
        }

        UpdateStatus.Text = "Installer starten...";
        App.Instance.InstallUpdate(path);
    }
}
