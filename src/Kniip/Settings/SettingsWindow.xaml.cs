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
using Kniip.Resources;

namespace Kniip.Settings;

public partial class SettingsWindow : Window
{
    private static string HotkeyHintText => Strings.Settings_HotkeyHint;

    private static readonly Dictionary<string, string> HotkeyNames = new()
    {
        ["Snip"] = Strings.Settings_HotkeyNameSnip,
        ["Window"] = Strings.Settings_HotkeyNameWindow,
        ["Record"] = Strings.Settings_HotkeyNameRecord,
        ["Gif"] = Strings.Settings_HotkeyNameGif,
        ["Clipboard"] = Strings.Settings_HotkeyNameClipboard,
        ["Ocr"] = "OCR",
    };

    private readonly AppSettings _settings;
    private readonly Dictionary<string, string> _hotkeys;
    private readonly Dictionary<string, Button> _hotkeyButtons;
    private string? _listeningFor;
    private string? _snipFolder;
    private string? _videoFolder;
    private readonly List<UploadDestination> _uploaders;
    private Guid? _defaultUploaderId;
    private bool _testingUploader;
    private bool _resetEditor;
    private bool _updateChecked;
    private ReleaseInfo? _update;
    private System.Threading.CancellationTokenSource? _updateCts;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        ThemeManager.Attach(this);
        _settings = settings;
        Title = string.Format(Strings.Settings_WindowTitle, AppIdentity.DisplayName);

        FolderText.Text = CaptureCoordinator.ScreenshotsFolder ?? Strings.Settings_ScreenshotsFolderMissing;
        AutostartBox.IsChecked = ShellIntegration.IsAutostartEnabled();
        OpenWithBox.IsChecked = ShellIntegration.IsOpenWithRegistered();

        WatchClipboardBox.Checked += (_, _) => OnlySnippingToolBox.IsEnabled = true;
        WatchClipboardBox.Unchecked += (_, _) => OnlySnippingToolBox.IsEnabled = false;

        _uploaders = settings.Uploaders.Select(SettingsStore.DeepCopy).ToList();
        _defaultUploaderId = settings.DefaultUploader?.Id;
        ShowUploaders();

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
            : Strings.Settings_HookFailed;
        Deactivated += (_, _) => StopListening();
        App.Instance.Updates.AvailabilityChanged += OnUpdateAvailabilityChanged;
        Closed += (_, _) =>
        {
            StopListening();
            _updateCts?.Cancel();
            App.Instance.Updates.AvailabilityChanged -= OnUpdateAvailabilityChanged;
        };
        ShowUpdateBadge();

        CurrentVersionText.Text = string.Format(Strings.Settings_InstalledVersion, UpdateChecker.CurrentVersion);
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
        UpdateIntervalSlider.Value = Math.Min(settings.UpdateCheckHours, UpdateIntervalSlider.Maximum);
        ShowUpdateInterval();
        MagnifierBox.IsChecked = settings.ShowMagnifier;

        _snipFolder = settings.SnipSaveFolder;
        _videoFolder = settings.VideoSaveFolder;
        ShowSaveFolders();
        SnipFolderTemplateBox.Text = settings.SnipFolderTemplate ?? string.Empty;
        SnipTemplateBox.Text = settings.SnipFileNameTemplate ?? string.Empty;
        VideoFolderTemplateBox.Text = settings.VideoFolderTemplate ?? string.Empty;
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
        FillChoices(VideoFpsCombo, new[] { 15, 30, 60 }, settings.RecordFps, fps => string.Format(Strings.Settings_Fps, fps));
        FillChoices(GifFpsCombo, new[] { 5, 10, 15, 20, 25 }, settings.GifFps, fps => string.Format(Strings.Settings_Fps, fps));
        FillChoices(GifWidthCombo, new[] { 0, 1920, 1280, 960, 800, 640, 480 }, settings.GifMaxWidth,
            width => width == 0 ? Strings.Settings_OriginalWidth : string.Format(Strings.Settings_MaxWidth, width));
        FillChoices(LanguageCombo, new[] { (int)AppLanguage.System, (int)AppLanguage.Dutch, (int)AppLanguage.English }, (int)settings.Language,
            value => (AppLanguage)value switch
            {
                AppLanguage.Dutch => "Nederlands",
                AppLanguage.English => "English",
                _ => Strings.Settings_LanguageAutomatic
            });
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

    private void UpdateInterval_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => ShowUpdateInterval();

    private void ShowUpdateInterval()
    {
        if (UpdateIntervalText == null)
        {
            return;
        }

        int hours = (int)UpdateIntervalSlider.Value;
        UpdateIntervalText.Text = hours == 1 ? Strings.Settings_EveryHour : string.Format(Strings.Settings_EveryHours, hours);
    }

    private static int SelectedValue(ComboBox combo, int fallback) =>
        combo.SelectedItem is ComboBoxItem { Tag: int value } ? value : fallback;

    private void ShowHotkey(string id) =>
        _hotkeyButtons[id].Content = HotkeyGesture.TryParse(_hotkeys[id], out HotkeyGesture gesture) ? gesture.DisplayText : Strings.Settings_NoHotkeyShort;

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
        _hotkeyButtons[id].Content = Strings.Settings_PressKey;
        HotkeyHint.Text = Strings.Settings_PressKeyHint;
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
            HotkeyHint.Text = string.Format(Strings.Settings_HotkeyTyping, pressed.DisplayText);
            return;
        }

        foreach ((string other, string hotkey) in _hotkeys)
        {
            if (other != id && HotkeyGesture.TryParse(hotkey, out HotkeyGesture taken) && taken == pressed)
            {
                HotkeyHint.Text = string.Format(Strings.Settings_HotkeyTaken, pressed.DisplayText, HotkeyNames[other]);
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
        text.Text = string.IsNullOrEmpty(folder) ? Strings.Settings_AskEveryTime : folder;
        text.ToolTip = folder;
        text.SetResourceReference(TextBlock.ForegroundProperty, string.IsNullOrEmpty(folder) ? "SubtleTextBrush" : "InkBrush");
    }

    private void Template_TextChanged(object sender, TextChangedEventArgs e) => ShowTemplatePreviews();

    private void ShowTemplatePreviews()
    {
        ShowTemplatePreview(SnipTemplatePreview, SnipFolderTemplateBox, SnipTemplateBox, Strings.Snip_DefaultName, ".png");
        ShowTemplatePreview(VideoTemplatePreview, VideoFolderTemplateBox, VideoTemplateBox, Strings.Screencast_DefaultName, ".mp4");
    }

    private static void ShowTemplatePreview(TextBlock preview, TextBox folderBox, TextBox fileBox, string defaultNameFormat, string extension)
    {
        string? folder = NullIfBlank(folderBox.Text);
        string? file = NullIfBlank(fileBox.Text);
        preview.Visibility = folder is null && file is null ? Visibility.Collapsed : Visibility.Visible;
        preview.Text = Strings.Settings_Example + FileNameTemplate.Preview(folder, file, FileNameTemplate.SampleWindow,
            string.Format(defaultNameFormat, DateTime.Now), extension);
    }

    /// <summary>Dropdown met de tokens per categorie; bij een submap ontbreekt Venster.</summary>
    private void InsertToken_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        string tag = (string)button.Tag;
        TextBox box = tag switch
        {
            "SnipFolder" => SnipFolderTemplateBox,
            "SnipFile" => SnipTemplateBox,
            "VideoFolder" => VideoFolderTemplateBox,
            _ => VideoTemplateBox
        };
        bool folder = tag.EndsWith("Folder", StringComparison.Ordinal);

        var menu = new ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (IGrouping<TemplateCategory, TemplateToken> group in FileNameTemplate.Tokens
                     .Where(t => !folder || t.Category != TemplateCategory.Window)
                     .GroupBy(t => t.Category))
        {
            var category = new MenuItem { Header = CategoryName(group.Key) };
            foreach (TemplateToken token in group)
            {
                var item = new MenuItem { Header = TokenRow(token) };
                item.Click += (_, _) => InsertToken(box, token.Insert);
                category.Items.Add(item);
            }

            menu.Items.Add(category);
        }

        menu.IsOpen = true;
    }

    private static string CategoryName(TemplateCategory category) => category switch
    {
        TemplateCategory.Window => Strings.Template_CategoryWindow,
        TemplateCategory.Time => Strings.Template_CategoryTime,
        TemplateCategory.Incremental => Strings.Template_CategoryIncremental,
        _ => Strings.Template_CategoryComputer
    };

    /// <summary>Token, voorbeeldwaarde en uitleg in drie uitgelijnde kolommen.</summary>
    private static Grid TokenRow(TemplateToken token)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "TokenCode" });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "TokenSample" });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "TokenHelp" });

        var code = new TextBlock { Text = token.Insert, FontFamily = new FontFamily("Consolas"), Margin = new Thickness(0, 0, 16, 0) };
        var sample = new TextBlock { Text = token.Sample, MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 16, 0) };
        sample.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
        var help = new TextBlock { Text = token.Help };
        Grid.SetColumn(sample, 1);
        Grid.SetColumn(help, 2);
        grid.Children.Add(code);
        grid.Children.Add(sample);
        grid.Children.Add(help);
        return grid;
    }

    private static void InsertToken(TextBox box, string token)
    {
        int caret = box.SelectionStart;
        box.SelectedText = token;
        box.CaretIndex = caret + token.Length;
        box.Focus();
    }

    private static string? NullIfBlank(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private void BrowseSaveFolder_Click(object sender, RoutedEventArgs e)
    {
        bool snip = sender is FrameworkElement { Tag: "Snip" };
        string? current = snip ? _snipFolder : _videoFolder;
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = snip ? Strings.Settings_ChooseSnipFolder : Strings.Settings_ChooseVideoFolder,
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

    private sealed record UploaderRow(UploadDestination Destination, bool IsDefault)
    {
        public string Title => Destination.Title;

        public string Subtitle => Destination.DisplayName == Destination.Title
            ? UploaderEditorWindow.TypeLabel(Destination)
            : UploaderEditorWindow.TypeLabel(Destination) + "  ·  " + Destination.DisplayName;

        public string Glyph => Destination switch
        {
            CustomUploaderDestination => "\uE774",
            _ => "\uE8B7"
        };
    }

    private static UploadDestination? RowOf(object sender) =>
        (sender as FrameworkElement)?.DataContext is UploaderRow row ? row.Destination : null;

    private void ShowUploaders()
    {
        if (_defaultUploaderId is not Guid id || _uploaders.All(u => u.Id != id))
        {
            _defaultUploaderId = _uploaders.FirstOrDefault()?.Id;
        }

        UploaderList.ItemsSource = _uploaders.Select(u => new UploaderRow(u, u.Id == _defaultUploaderId)).ToList();
        NoUploadersText.Visibility = _uploaders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UploaderRow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && RowOf(sender) is { } destination)
        {
            EditUploader(destination);
        }
    }

    private void NewUploader_Click(object sender, RoutedEventArgs e) =>
        NewUploader((sender as FrameworkElement)?.Tag switch
        {
            "sftp" => new SftpDestination(),
            "scp" => new ScpDestination(),
            _ => new FtpDestination()
        });

    private void NewUploader(UploadDestination destination)
    {
        var editor = new UploaderEditorWindow(destination) { Owner = this };
        if (editor.ShowDialog() != true)
        {
            return;
        }

        _uploaders.Add(editor.Destination);
        ShowUploaders();
        ShowUploaderStatus(string.Format(Strings.Settings_UploadAdded, editor.Destination.Title));
    }

    private void EditUploader_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } destination)
        {
            EditUploader(destination);
        }
    }

    private void EditUploader(UploadDestination destination)
    {
        var editor = new UploaderEditorWindow(destination) { Owner = this };
        if (editor.ShowDialog() != true)
        {
            return;
        }

        _uploaders[_uploaders.IndexOf(destination)] = editor.Destination;
        ShowUploaders();
        ShowUploaderStatus(null);
    }

    private void DefaultUploader_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } destination)
        {
            _defaultUploaderId = destination.Id;
            ShowUploaders();
        }
    }

    private void UploaderMore_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } destination)
        {
            return;
        }

        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        void Add(string header, string? tip, bool enabled, Action action)
        {
            var item = new MenuItem { Header = header, ToolTip = tip, IsEnabled = enabled };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        Add(Strings.Settings_Test, Strings.Settings_TestTip, destination.IsConfigured && !_testingUploader, () => TestUploader(destination));
        Add(Strings.Settings_UploadDuplicate, null, true, () => DuplicateUploader(destination));
        Add(Strings.Settings_UploadExport, Strings.Settings_UploadExportTip, destination is CustomUploaderDestination,
            () => ExportUploader((CustomUploaderDestination)destination));
        menu.Items.Add(new Separator());
        Add(Strings.Settings_RemoveUpload, null, true, () => RemoveUploader(destination));
        menu.IsOpen = true;
    }

    private void DuplicateUploader(UploadDestination destination)
    {
        UploadDestination copy = SettingsStore.DeepCopy(destination);
        copy.Id = Guid.NewGuid();
        copy.Name = string.Format(Strings.Settings_UploadCopyName, destination.Title);
        _uploaders.Insert(_uploaders.IndexOf(destination) + 1, copy);
        ShowUploaders();
    }

    private void ExportUploader(CustomUploaderDestination custom)
    {

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = Strings.Settings_UploadExport,
            Filter = Strings.Settings_ExportFilter,
            FileName = string.Concat(custom.Title.Split(System.IO.Path.GetInvalidFileNameChars())) + ".sxcu"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            System.IO.File.WriteAllText(dialog.FileName, UploaderImporter.ToSxcu(custom));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, string.Format(Strings.Settings_BackupFailed, dialog.FileName, ex.Message), "kniip",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ShowUploaderStatus(string.Format(Strings.Settings_UploadExported, dialog.FileName));
        if (custom.HasSecrets)
        {
            MessageBox.Show(this, Strings.Settings_UploadExportWarning, "kniip", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShowUploaderStatus(string? message, string? url = null, string? suffix = null)
    {
        UploaderStatus.Inlines.Clear();
        UploaderStatus.Inlines.Add(new Run(message ?? string.Empty));
        if (url != null)
        {
            var link = new Hyperlink(new Run(url)) { ToolTip = Strings.Settings_OpenInBrowser };
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
            Title = Strings.Settings_ImportTitle,
            Filter = Strings.Settings_ImportFilter,
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
            CustomUploaderDestination imported = UploaderImporter.FromFile(dialog.FileName);
            _uploaders.Add(imported);
            ShowUploaders();
            ShowUploaderStatus(string.Format(Strings.Settings_Imported, imported.Title));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, string.Format(Strings.Settings_ImportFailed, dialog.FileName, ex.Message), "kniip",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BackupSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = Strings.Settings_BackupCreate,
            Filter = Strings.Settings_BackupFilter,
            FileName = $"{AppIdentity.Key.ToLowerInvariant()}-{Strings.Settings_BackupFileName}-{DateTime.Now:yyyyMMdd}.json"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        bool plain = PlainTokenBox.IsChecked == true;
        try
        {
            SettingsStore.Export(App.Instance.Settings, dialog.FileName, plain);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, string.Format(Strings.Settings_BackupFailed, dialog.FileName, ex.Message), "kniip",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        BackupStatus.Text = string.Format(Strings.Settings_BackupSaved, dialog.FileName);
        BackupStatus.Visibility = Visibility.Visible;
        if (plain && App.Instance.Settings.Uploaders.Any(u => u.HasSecrets))
        {
            MessageBox.Show(this, Strings.Settings_BackupPlainTokenWarning, "kniip", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RestoreSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Strings.Settings_Restore,
            Filter = Strings.Settings_BackupFilter
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        AppSettings restored;
        try
        {
            restored = SettingsStore.Import(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, string.Format(Strings.Settings_RestoreFailed, dialog.FileName, ex.Message), "kniip",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (MessageBox.Show(this, Strings.Settings_RestoreConfirm, "kniip", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        App.Instance.ReplaceSettings(restored);
        Close();
        App.Instance.Restart();
    }

    private async void TestUploader(UploadDestination config)
    {
        _testingUploader = true;
        ShowUploaderStatus(Strings.Settings_TestUploading);
        try
        {
            using System.IO.MemoryStream png = ImageExporter.EncodePng(UploadTestImage.Create());
            UploadResult result = await UploaderFactory.UploadAsync(config, png, "kniip-test.png", "image/png");
            bool copied = ClipboardWriter.SetText(result.Url);
            ShowUploaderStatus(Strings.Settings_TestSucceeded, result.Url, copied ? Strings.Settings_TestCopied : null);
        }
        catch (OperationCanceledException)
        {
            ShowUploaderStatus(null);
        }
        catch (Exception ex)
        {
            ShowUploaderStatus(null);
            MessageBox.Show(this, string.Format(Strings.Settings_TestFailed, ex.Message), "kniip", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _testingUploader = false;
        }
    }

    private void RemoveUploader(UploadDestination destination)
    {
        _uploaders.Remove(destination);
        ShowUploaders();
        ShowUploaderStatus(null);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.Uploaders = _uploaders;
        _settings.DefaultUploaderId = _defaultUploaderId;
        _settings.SnipSaveFolder = _snipFolder;
        _settings.VideoSaveFolder = _videoFolder;
        _settings.SnipFolderTemplate = NullIfBlank(SnipFolderTemplateBox.Text);
        _settings.SnipFileNameTemplate = NullIfBlank(SnipTemplateBox.Text);
        _settings.VideoFolderTemplate = NullIfBlank(VideoFolderTemplateBox.Text);
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
        _settings.UpdateCheckHours = (int)UpdateIntervalSlider.Value;
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
        _settings.Language = (AppLanguage)SelectedValue(LanguageCombo, (int)_settings.Language);

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
            MessageBox.Show(this, string.Format(Strings.Settings_IntegrationFailed, ex.Message), "kniip",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        App.Instance.SaveSettings();
        Close();

        if (UiLanguage.IsDutchFor(_settings.Language) != UiLanguage.IsDutch
            && MessageBox.Show(Strings.Settings_LanguageRestart, "kniip", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            App.Instance.Restart();
        }
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
        UpdateStatus.Text = Strings.Settings_CheckingUpdates;
        try
        {
            IReadOnlyList<ReleaseInfo> releases = await UpdateChecker.GetReleasesAsync(cts.Token);
            ShowReleases(releases);
        }
        catch (UpdateException ex)
        {
            UpdateStatus.Text = ex.Message;
            ShowChangelog(Array.Empty<ReleaseInfo>(), Strings.Settings_NoChangelog);
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
                ? Strings.Settings_NoReleases
                : string.Format(Strings.Settings_UpToDate, current);
            ReleaseInfo? notes = releases.FirstOrDefault(r => r.Version == current) ?? releases.FirstOrDefault();
            ShowChangelog(notes == null ? Array.Empty<ReleaseInfo>() : new[] { notes }, Strings.Settings_NoChangelog);
            return;
        }

        if (AppIdentity.IsTest)
        {
            UpdateStatus.Text = string.Format(Strings.Settings_UpdateTestBuild, newer[0].Version);
            ShowChangelog(newer, null);
            return;
        }

        _update = newer.FirstOrDefault(r => r.InstallerUrl != null);
        if (_update != null)
        {
            UpdateStatus.Text = string.Format(Strings.Settings_UpdateAvailable, _update.Version);
            UpdateButton.Content = string.Format(Strings.Settings_UpdateTo, _update.Version);
            UpdateButton.Visibility = Visibility.Visible;
        }
        else
        {
            UpdateStatus.Text = string.Format(Strings.Settings_UpdateNoInstaller, newer[0].Version);
        }

        ShowChangelog(newer, null);
    }

    private void ShowChangelog(IReadOnlyList<ReleaseInfo> releases, string? emptyText)
    {
        FlowDocument document = MarkdownRenderer.CreateDocument();
        var culture = System.Globalization.CultureInfo.CurrentUICulture;
        var baseUri = new Uri(UpdateChecker.RepositoryUrl);
        foreach (ReleaseInfo release in releases)
        {
            Paragraph heading = MarkdownRenderer.Heading(string.Format(Strings.Settings_VersionHeading, release.Version), 17,
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
                document.Blocks.Add(new Paragraph(new Run(Strings.Settings_NoDescription))
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
        string message = string.Format(Strings.Settings_UpdateConfirm, release.Version);
        if (!installed)
        {
            message += "\n\n" + string.Format(Strings.Settings_UpdateNotInstalled, installFolder);
        }

        if (MessageBox.Show(this, message, Strings.Settings_UpdateConfirmTitle, MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK)
        {
            return;
        }

        var cts = new System.Threading.CancellationTokenSource();
        _updateCts = cts;
        UpdateButton.IsEnabled = false;
        CheckUpdateButton.IsEnabled = false;
        DownloadProgress.Value = 0;
        DownloadProgress.Visibility = Visibility.Visible;
        UpdateStatus.Text = string.Format(Strings.Settings_UpdateDownloading, release.Version);
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
            UpdateStatus.Text = string.Format(Strings.Settings_UpdateAvailable, release.Version);
            MessageBox.Show(this, string.Format(Strings.Settings_UpdateDownloadFailed, ex.Message), "kniip",
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

        UpdateStatus.Text = Strings.Settings_StartingInstaller;
        App.Instance.InstallUpdate(path);
    }
}
