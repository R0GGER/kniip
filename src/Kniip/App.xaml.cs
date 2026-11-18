using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Kniip.Capture;
using Kniip.Editor;
using Kniip.History;
using Kniip.Infrastructure;
using Kniip.Interop;
using Kniip.Notifications;
using Kniip.Recording;
using Kniip.Resources;
using Kniip.Settings;
using Kniip.Update;
using Kniip.Upload;
using Kniip.Workflows;
using Microsoft.Win32;
using Screen = System.Windows.Forms.Screen;

namespace Kniip;

public partial class App : Application
{
    /// <summary>Waar een rechthoekknipsel heen gaat: naar de editor, naar tekstherkenning of naar een workflow.</summary>
    private enum SnipTarget { Editor, Ocr, Workflow }

    private SingleInstance? _singleInstance;
    private TrayIcon? _tray;
    private SettingsWindow? _settingsWindow;
    private HistoryWindow? _historyWindow;
    private EditorShellWindow? _editorShell;
    private readonly List<RegionCaptureWindow> _snipWindows = new();
    private List<SnappedWindow> _snipSnapshot = new();
    private CountdownWindow? _snipCountdown;
    private SnipTarget _snipTarget;
    private Action? _snipFinished;
    private Action<BitmapSource, WindowInfo?, int?>? _workflowSnip;
    private Action<string, WindowInfo?>? _workflowRecording;
    private WorkflowBarWindow? _workflowBar;
    private uint? _ignoredClipboardSequence;

    public static App Instance => (App)Current;

    public AppSettings Settings { get; private set; } = new();

    internal CaptureCoordinator Capture { get; private set; } = null!;

    internal RecordingController Recorder { get; private set; } = null!;

    internal WorkflowRunner Workflows { get; private set; } = null!;

    /// <summary>Toetsenbordhaak voor alle sneltoetsen (knipsels en screencasts).</summary>
    internal GlobalHotkeyHook? HotkeyHook { get; private set; }

    /// <summary>Achtergrondcontrole op nieuwe versies; stuurt de blauwe stip bij Instellingen.</summary>
    public UpdateNotifier Updates { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        UiLanguage.Apply();
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        string[] args = e.Args;
        if (HandleMaintenanceArguments(args))
        {
            Shutdown();
            return;
        }

        _singleInstance = new SingleInstance();
        if (!_singleInstance.TryAcquire())
        {
            SingleInstance.SendToPrimary(args);
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        System.Windows.Forms.Application.EnableVisualStyles();
        try
        {
            ShellIntegration.MigrateLegacyRegistration();
        }
        catch (Exception)
        {
            // Een mislukte opruimactie in het register mag het starten niet blokkeren.
        }

        Settings = SettingsStore.Load();
        UiLanguage.Apply(Settings.Language);
        ThemeManager.Apply(Settings.ColorMode);
        AddExampleWorkflows();
        Workflows = new WorkflowRunner(this);

        Capture = new CaptureCoordinator(Dispatcher);
        Capture.ApplySettings(Settings);
        Capture.CaptureReceived += OnCaptureReceived;

        RecordingSession.CleanupTempFolder();
        Recorder = new RecordingController(Settings, Dispatcher);
        Recorder.StageChanged += OnRecordingStageChanged;
        Recorder.RecordingFinished += OnRecordingFinished;
        Recorder.Notify += (title, text) => _tray?.ShowBalloon(title, text);

        _tray = new TrayIcon();
        _tray.OpenFromClipboardRequested += OpenFromClipboard;
        _tray.OpenRequested += OpenKniip;
        _tray.OpenFileRequested += OpenFileWithDialog;
        _tray.UploadClipboardRequested += () => _ = QuickUploader.UploadClipboardAsync();
        _tray.UploadFilesRequested += UploadFilesWithDialog;
        _tray.CanUpload = () => Settings.DefaultUploader != null;
        _tray.SnipRequested += BeginRegionSnip;
        _tray.WindowSnipRequested += BeginWindowSnip;
        _tray.TextRecognitionRequested += () => BeginTextSnip();
        _tray.SetCaptureDelay(Settings.CaptureDelaySeconds);
        _tray.CaptureDelayChanged += seconds =>
        {
            Settings.CaptureDelaySeconds = seconds;
            SettingsStore.Save(Settings);
        };
        _tray.RecordRequested += () =>
        {
            if (Recorder.Stage == RecordingStage.Idle)
            {
                Recorder.Toggle(ScreencastFormat.Mp4);
            }
            else
            {
                Recorder.TrayAction();
            }
        };
        _tray.GifRequested += () => Recorder.Toggle(ScreencastFormat.Gif);
        _tray.SettingsRequested += ShowSettings;
        _tray.HistoryRequested += ShowHistory;
        _tray.FoldersProvider = TrayFolders;
        _tray.WorkflowsProvider = TrayWorkflows;
        _tray.WorkflowRequested += id => RunWorkflow(id);
        _tray.WorkflowBarRequested += ToggleWorkflowBar;
        _tray.WorkflowSettingsRequested += ShowWorkflowSettings;
        _tray.CanUseWorkflowBar = () => CanShowWorkflowBar;
        _tray.ExitRequested += ExitApplication;
        _tray.PauseChanged += _ => UpdateCapturePaused();
        _tray.ApplyTheme(ThemeManager.IsDark);
        ThemeManager.Changed += () => _tray?.ApplyTheme(ThemeManager.IsDark);

        HotkeyHook = new GlobalHotkeyHook(Dispatcher);
        ApplyHotkeys();
        ApplyWorkflowBar();
        Updates.AvailabilityChanged += (_, _) => _tray?.SetUpdateAvailable(Updates.Available?.Tag);
        Updates.Apply(Settings.CheckForUpdates, TimeSpan.FromHours(Settings.UpdateCheckHours));

        _singleInstance.StartServer(received => Dispatcher.BeginInvoke(() => HandleArguments(received, false)));
        HandleArguments(args, true);
    }

    private static bool HandleMaintenanceArguments(string[] args)
    {
        if (args.Contains("--register-openwith", StringComparer.OrdinalIgnoreCase))
        {
            ShellIntegration.RegisterOpenWith();
            return true;
        }

        if (args.Contains("--unregister", StringComparer.OrdinalIgnoreCase))
        {
            ShellIntegration.UnregisterOpenWith();
            ShellIntegration.SetAutostart(false);
            ShellIntegration.SetSendTo(false);
            return true;
        }

        return false;
    }

    private void HandleArguments(string[] args, bool isFirstStart)
    {
        string[] files = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
        if (args.Contains("--upload", StringComparer.OrdinalIgnoreCase))
        {
            _ = QuickUploader.UploadFilesAsync(files);
            return;
        }

        foreach (string file in files)
        {
            OpenFile(file);
        }

        if (args.Contains("--updated", StringComparer.OrdinalIgnoreCase))
        {
            _tray?.ShowBalloon(Strings.App_UpdatedTitle, string.Format(Strings.App_UpdatedText, UpdateChecker.CurrentVersion));
            return;
        }

        if (args.Contains("--settings", StringComparer.OrdinalIgnoreCase))
        {
            ShowSettings();
            return;
        }

        if (files.Length > 0 || args.Contains("--tray", StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        if (isFirstStart)
        {
            string? hotkey = HotkeyDisplay(Settings.SnipHotkey);
            _tray?.ShowBalloon(string.Format(Strings.App_ActiveTitle, AppIdentity.DisplayName), hotkey == null
                ? Strings.App_ActiveTextNoHotkey
                : string.Format(Strings.App_ActiveText, hotkey));
        }

        // Opnieuw starten terwijl de editor (eventueel verborgen) tabbladen heeft: die tonen in plaats van een extra leeg canvas.
        if (_editorShell != null)
        {
            ShowEditor();
        }
        else
        {
            OpenBlankEditor();
        }
    }

    private void OnCaptureReceived(BitmapSource image)
    {
        switch (Settings.Action)
        {
            case CaptureAction.OpenEditor:
                OpenEditor(image);
                break;
            case CaptureAction.Notify:
                SnipToast.ShowFor(image, () => OpenEditor(image));
                break;
        }
    }

    /// <param name="window">Programmanaam en titel van het geknipte venster, voor %pn en %t in het naamsjabloon.</param>
    /// <param name="windowCornerRadius">Alleen bij een vensterknipsel: straal van de vensterhoeken, voor de vensterschaduw.</param>
    public void OpenEditor(BitmapSource image, WindowInfo? window = null, int? windowCornerRadius = null) =>
        AddEditorTab(new EditorView(image, Settings, window: window, windowCornerRadius: windowCornerRadius));

    private void AddEditorTab(EditorView view)
    {
        if (_editorShell == null)
        {
            _editorShell = new EditorShellWindow(Settings);
            _editorShell.Closed += (_, _) => _editorShell = null;
        }

        _editorShell.AddTab(view);
        ShowEditor();
    }

    /// <summary>Het editorvenster tonen, ook als het met behouden tabbladen verborgen is.</summary>
    public void ShowEditor()
    {
        if (_editorShell == null)
        {
            return;
        }

        _editorShell.Show();
        BringToFront(_editorShell);
    }

    /// <summary>Open kniip uit het systeemvak: de editor met de bestaande tabbladen, anders met een leeg canvas.</summary>
    private void OpenKniip()
    {
        if (_editorShell != null)
        {
            ShowEditor();
        }
        else
        {
            OpenBlankEditor();
        }
    }

    /// <summary>Nieuw rechthoekknipsel vanuit de editor; het resultaat komt als nieuw tabblad.</summary>
    /// <param name="finished">Na het kiezen of annuleren, vóórdat het tabblad opent; bijvoorbeeld om de editor terug te zetten.</param>
    public void StartNewSnip(Action? finished = null)
    {
        // Even wachten zodat de weggezette editor niet op het nieuwe knipsel staat.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_snipCountdown != null || _snipWindows.Count > 0 || Recorder.Stage == RecordingStage.Selecting)
            {
                finished?.Invoke();
                return;
            }

            _snipTarget = SnipTarget.Editor;
            _snipFinished = finished;
            BeginSnip(pickWindow: false);
        };
        timer.Start();
    }

    /// <summary>Bevriest alle schermen (na de ingestelde vertraging) en laat een rechthoek slepen; het resultaat opent in de editor.</summary>
    public void BeginRegionSnip() => BeginDelayedSnip(pickWindow: false);

    /// <summary>Bevriest alle schermen (na de ingestelde vertraging) en laat een venster aanklikken; het resultaat opent in de editor.</summary>
    public void BeginWindowSnip() => BeginDelayedSnip(pickWindow: true);

    /// <summary>Als een rechthoekknipsel, maar de herkende tekst opent in een apart venster in plaats van de editor.</summary>
    /// <param name="finished">Na het kiezen of annuleren, vóórdat het tekstvenster opent; bijvoorbeeld om de editor terug te zetten.</param>
    public void BeginTextSnip(Action? finished = null)
    {
        if (_snipCountdown != null || _snipWindows.Count > 0 || Recorder.Stage == RecordingStage.Selecting)
        {
            finished?.Invoke();
            return;
        }

        _snipFinished = finished;
        BeginDelayedSnip(pickWindow: false, SnipTarget.Ocr);
    }

    private void RunSnipFinished()
    {
        Action? finished = _snipFinished;
        _snipFinished = null;
        finished?.Invoke();
    }

    private void BeginDelayedSnip(bool pickWindow, SnipTarget target = SnipTarget.Editor)
    {
        if (_snipCountdown == null && _snipWindows.Count == 0)
        {
            _snipTarget = target;
        }

        if (_snipCountdown != null)
        {
            return;
        }

        int seconds = Settings.CaptureDelaySeconds;
        if (seconds <= 0 || _snipWindows.Count > 0)
        {
            BeginSnip(pickWindow);
            return;
        }

        var countdown = new CountdownWindow(seconds);
        countdown.Elapsed += () =>
        {
            EndCountdown();
            // Even wachten tot het aftelvenster echt van het scherm is.
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                BeginSnip(pickWindow);
            };
            timer.Start();
        };
        countdown.Cancelled += () =>
        {
            EndCountdown();
            RunSnipFinished();
        };
        _snipCountdown = countdown;
        ApplyHotkeys();
        countdown.Start();
    }

    private void EndCountdown()
    {
        _snipCountdown = null;
        ApplyHotkeys();
    }

    private void BeginSnip(bool pickWindow)
    {
        if (_snipWindows.Count > 0 || _snipCountdown != null || Recorder.Stage == RecordingStage.Selecting)
        {
            return;
        }

        NativeMethods.GetCursorPos(out NativeMethods.POINT cursor);
        var cursorPoint = new System.Drawing.Point(cursor.X, cursor.Y);

        // Eerst alle schermen vastleggen, dan pas vensters tonen, anders staat de overlay in het beeld.
        _snipSnapshot = PhysicalWindow.Snapshot(includeOwn: true);
        List<System.Drawing.Rectangle>? windows = pickWindow ? _snipSnapshot.Select(w => w.Bounds).ToList() : null;
        var frozen = Screen.AllScreens.Select(screen => (screen, ScreenGrabber.Capture(screen.Bounds))).ToList();
        foreach ((Screen screen, BitmapSource image) in frozen)
        {
            var window = new RegionCaptureWindow(screen, image, windows, Settings.ShowMagnifier);
            window.Finished += OnRegionSnipFinished;
            _snipWindows.Add(window);
            window.Show();
        }

        RegionCaptureWindow? active = _snipWindows.FirstOrDefault(w => w.ContainsCursor(cursorPoint)) ?? _snipWindows.FirstOrDefault();
        active?.Activate();
        active?.Focus();

        // Na een sneltoets in een ander programma krijgt de overlay vaak geen toetsenbordfocus;
        // Esc en Enter lopen daarom via de haak.
        if (HotkeyHook is { IsActive: true } hook)
        {
            hook.Capture = OnRegionSnipKey;
        }
    }

    private void OnRegionSnipKey(HotkeyGesture pressed)
    {
        NativeMethods.GetCursorPos(out NativeMethods.POINT cursor);
        var cursorPoint = new System.Drawing.Point(cursor.X, cursor.Y);
        RegionCaptureWindow? target = _snipWindows.FirstOrDefault(w => w.ContainsCursor(cursorPoint)) ?? _snipWindows.FirstOrDefault();
        if (target == null || pressed.Modifiers != System.Windows.Input.ModifierKeys.None)
        {
            return;
        }

        if (pressed.Key == System.Windows.Input.Key.Escape)
        {
            target.Cancel();
        }
        else if (pressed.Key == System.Windows.Input.Key.Enter)
        {
            target.SelectWholeScreen();
        }
    }

    private void OnRegionSnipFinished(BitmapSource? image)
    {
        if (HotkeyHook != null)
        {
            HotkeyHook.Capture = null;
        }

        System.Drawing.Rectangle? area = _snipWindows.Select(w => w.SelectedArea).FirstOrDefault(a => a != null);
        bool pickedWindow = _snipWindows.Any(w => w.PickedWindow);
        foreach (RegionCaptureWindow window in _snipWindows)
        {
            window.Finished -= OnRegionSnipFinished;
            window.Close();
        }

        _snipWindows.Clear();
        MemoryRelief.Schedule();
        List<SnappedWindow> snapshot = _snipSnapshot;
        _snipSnapshot = new();
        SnipTarget target = _snipTarget;
        _snipTarget = SnipTarget.Editor;
        Action<BitmapSource, WindowInfo?, int?>? workflow = _workflowSnip;
        _workflowSnip = null;
        RunSnipFinished();
        if (image != null && target == SnipTarget.Ocr)
        {
            BringToFront(OcrResultWindow.Open(image));
        }
        else if (image != null)
        {
            WindowInfo? window = area is { } a ? PhysicalWindow.WindowAt(snapshot, a) : null;
            int? cornerRadius = pickedWindow && area is { } picked ? PhysicalWindow.CornerRadiusAt(snapshot, picked) : null;
            if (target == SnipTarget.Workflow && workflow != null)
            {
                workflow(image, window, cornerRadius);
            }
            else
            {
                OpenEditor(image, window, cornerRadius);
            }
        }
    }

    /// <summary>Knipsel voor een workflow; het resultaat gaat naar <paramref name="captured"/> in plaats van de editor.</summary>
    /// <param name="finished">Na het kiezen of annuleren, ook als het aftellen wordt afgebroken.</param>
    /// <returns>False als er al een knipsel of screencast wordt gekozen.</returns>
    internal bool BeginWorkflowSnip(bool pickWindow, Action<BitmapSource, WindowInfo?, int?> captured, Action finished)
    {
        if (_snipCountdown != null || _snipWindows.Count > 0 || Recorder.Stage == RecordingStage.Selecting)
        {
            return false;
        }

        _workflowSnip = captured;
        _snipFinished = () =>
        {
            _workflowSnip = null;
            finished();
        };
        BeginDelayedSnip(pickWindow, SnipTarget.Workflow);
        return true;
    }

    /// <summary>Screencast voor een workflow; de opname gaat naar <paramref name="finished"/> in plaats van het screencastvenster.</summary>
    /// <returns>False als er al een screencast loopt.</returns>
    internal bool BeginWorkflowRecording(ScreencastFormat format, Action<string, WindowInfo?> finished)
    {
        if (Recorder.Stage != RecordingStage.Idle || _snipWindows.Count > 0)
        {
            return false;
        }

        _workflowRecording = finished;
        Recorder.Toggle(format);
        return true;
    }

    private void OnRecordingFinished(string path, ScreencastFormat? format, WindowInfo? recordedWindow)
    {
        if (_workflowRecording is { } workflow)
        {
            _workflowRecording = null;
            workflow(path, recordedWindow);
            return;
        }

        OpenScreencast(path, format, recordedWindow);
    }

    /// <summary>Stap Bewerken van een workflow: het knipsel als tabblad met een knop Doorgaan.</summary>
    internal void OpenWorkflowEditor(BitmapSource image, WindowInfo? window, int? windowCornerRadius, WorkflowContinuation continuation)
    {
        var view = new EditorView(image, Settings, window: window, windowCornerRadius: windowCornerRadius);
        view.AttachWorkflow(continuation);
        AddEditorTab(view);
    }

    /// <summary>Stap Bewerken van een workflow: de opname in het screencastvenster met een knop Doorgaan.</summary>
    internal void OpenWorkflowScreencast(string path, ScreencastFormat? format, WindowInfo? recordedWindow, WorkflowContinuation continuation)
    {
        var window = new ScreencastWindow(path, Settings, format, recordedWindow);
        window.AttachWorkflow(continuation);
        window.Show();
        BringToFront(window);
    }

    public void RunWorkflow(Guid id)
    {
        if (Settings.Workflows.FirstOrDefault(w => w.Id == id) is { } workflow)
        {
            Workflows.Run(workflow);
        }
    }

    /// <summary>Bij de eerste start met workflows twee voorbeelden toevoegen; niet-hernoemde voorbeelden volgen de taal.</summary>
    private void AddExampleWorkflows()
    {
        if (Settings.WorkflowExamplesAdded)
        {
            if (WorkflowDefinition.LocalizeExampleNames(Settings.Workflows))
            {
                SettingsStore.Save(Settings);
            }

            return;
        }

        if (Settings.Workflows.Count == 0)
        {
            Settings.Workflows.AddRange(WorkflowDefinition.Examples());
        }

        Settings.WorkflowExamplesAdded = true;
        SettingsStore.Save(Settings);
    }

    private IReadOnlyList<TrayWorkflow> TrayWorkflows() => Settings.Workflows
        .Where(w => w.ShowInTray)
        .Select(w => new TrayWorkflow(w.Id, w.Title, w.Glyph, w.IconFile, HotkeyDisplay(w.Hotkey), w.Summary(Settings.Uploaders)))
        .ToList();

    internal bool WorkflowBarShown => _workflowBar != null;

    /// <summary>De balk heeft alleen zin als er een workflow met "In de workflowbalk" is.</summary>
    internal bool CanShowWorkflowBar => Settings.Workflows.Any(w => w.ShowInBar);

    internal void ToggleWorkflowBar()
    {
        Settings.WorkflowBarVisible = _workflowBar == null;
        SettingsStore.Save(Settings);
        ApplyWorkflowBar();
    }

    /// <summary>Workflowbalk tonen of verbergen volgens de instellingen, en de knoppen bijwerken.</summary>
    private void ApplyWorkflowBar()
    {
        bool show = Settings.WorkflowBarVisible && Settings.Workflows.Any(w => w.ShowInBar);
        if (!show)
        {
            _workflowBar?.Close();
            _workflowBar = null;
            _tray?.SetWorkflowBarShown(false);
            return;
        }

        if (_workflowBar == null)
        {
            _workflowBar = new WorkflowBarWindow(Settings);
            _workflowBar.Closed += (_, _) =>
            {
                if (_workflowBar == null)
                {
                    return;
                }

                // Met het kruisje gesloten.
                _workflowBar = null;
                Settings.WorkflowBarVisible = false;
                SettingsStore.Save(Settings);
                _tray?.SetWorkflowBarShown(false);
            };
            _workflowBar.Show();
        }

        _workflowBar.ShowWorkflows(Settings.Workflows.Where(w => w.ShowInBar).ToList(), HotkeyDisplay);
        _tray?.SetWorkflowBarShown(true);
    }

    private string? HotkeyDisplay(string? hotkey) =>
        HotkeyHook is { IsActive: true } && HotkeyGesture.TryParse(hotkey, out HotkeyGesture gesture)
            ? gesture.DisplayText
            : null;

    private void ApplyHotkeys()
    {
        if (HotkeyHook != null)
        {
            var bindings = new Dictionary<HotkeyGesture, Action>();
            void Bind(string? hotkey, Action action)
            {
                if (HotkeyGesture.TryParse(hotkey, out HotkeyGesture gesture))
                {
                    bindings.TryAdd(gesture, action);
                }
            }

            Bind(Settings.SnipHotkey, BeginRegionSnip);
            Bind(Settings.WindowHotkey, BeginWindowSnip);
            Bind(Settings.RecordHotkey, () => Recorder.Toggle(ScreencastFormat.Mp4));
            Bind(Settings.GifHotkey, () => Recorder.Toggle(ScreencastFormat.Gif));
            Bind(Settings.ClipboardHotkey, OpenFromClipboard);
            Bind(Settings.OcrHotkey, () => BeginTextSnip());
            Bind(Settings.UploadClipboardHotkey, () => _ = QuickUploader.UploadClipboardAsync());
            foreach (WorkflowDefinition workflow in Settings.Workflows)
            {
                Guid id = workflow.Id;
                Bind(workflow.Hotkey, () => RunWorkflow(id));
            }

            if (_snipCountdown is { } countdown)
            {
                // Alleen tijdens het aftellen; daarna moet Esc gewoon naar het actieve programma.
                bindings[new HotkeyGesture(System.Windows.Input.ModifierKeys.None, System.Windows.Input.Key.Escape)] = countdown.Cancel;
            }

            HotkeyHook.Bindings = bindings;
        }

        _tray?.SetHotkeys(HotkeyDisplay(Settings.SnipHotkey), HotkeyDisplay(Settings.WindowHotkey),
            HotkeyDisplay(Settings.RecordHotkey), HotkeyDisplay(Settings.GifHotkey), HotkeyDisplay(Settings.ClipboardHotkey),
            HotkeyDisplay(Settings.OcrHotkey), HotkeyDisplay(Settings.UploadClipboardHotkey));
    }

    /// <summary>Start een screencast: eerst het gebied kiezen, daarna de knoppenbalk.</summary>
    public void StartRecording()
    {
        if (Recorder.Stage == RecordingStage.Idle)
        {
            Recorder.Toggle();
        }
    }

    public void StopRecording()
    {
        if (Recorder.Stage is RecordingStage.Recording or RecordingStage.Paused)
        {
            Recorder.Toggle();
        }
    }

    private void OnRecordingStageChanged(RecordingStage stage)
    {
        string? text = stage switch
        {
            RecordingStage.Idle => null,
            RecordingStage.Recording or RecordingStage.Paused => Strings.Recording_Stop,
            RecordingStage.Finishing => Strings.Recording_Finishing,
            _ => Strings.Recording_CancelTray
        };
        _tray?.SetRecordItem(text, stage is RecordingStage.Recording or RecordingStage.Paused);
        UpdateCapturePaused();
        if (stage == RecordingStage.Idle && _workflowRecording != null)
        {
            // Een geslaagde opname meldt zich direct na Idle; daarna is de workflow geannuleerd of weggegooid.
            Dispatcher.BeginInvoke(() =>
            {
                if (Recorder.Stage == RecordingStage.Idle)
                {
                    _workflowRecording = null;
                }
            });
        }
    }

    /// <summary>Tijdens een screencast geen knipsels oppikken.</summary>
    private void UpdateCapturePaused() =>
        Capture.Paused = (_tray?.IsPaused ?? false) || Recorder.Stage != RecordingStage.Idle;

    private void OpenScreencast(string path, ScreencastFormat? format, WindowInfo? recordedWindow)
    {
        var window = new ScreencastWindow(path, Settings, format, recordedWindow);
        window.Show();
        BringToFront(window);
    }

    /// <summary>Het knipsel op het klembord is weggegooid; pas na een nieuwe kopie weer openen.</summary>
    public void IgnoreCurrentClipboard() => _ignoredClipboardSequence = NativeMethods.GetClipboardSequenceNumber();

    public void OpenFromClipboard()
    {
        BitmapSource? image = _ignoredClipboardSequence == NativeMethods.GetClipboardSequenceNumber()
            ? null
            : ClipboardImageReader.TryRead(includeFiles: true);
        if (image == null)
        {
            OpenBlankEditor();
            return;
        }

        OpenEditor(image);
    }

    /// <summary>Tabblad met een leeg, transparant canvas, voor als er (nog) geen knipsel is.</summary>
    public void OpenBlankEditor() => AddEditorTab(CreateBlankEditor());

    public EditorView CreateBlankEditor()
    {
        const int width = 1280, height = 720;
        var pixels = new byte[width * height * 4];
        BitmapSource blank = BitmapSource.Create(width, height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, width * 4);
        return new EditorView(blank, Settings, isBlank: true);
    }

    public void OpenFile(string path)
    {
        try
        {
            OpenEditor(PixelUtil.LoadFile(path));
        }
        catch (Exception ex)
        {
            MessageBox.Show(string.Format(Strings.Common_CannotOpenFile, Path.GetFileName(path), ex.Message), "kniip",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenFileWithDialog()
    {
        if (PickImageFile(null) is { } path)
        {
            OpenFile(path);
        }
    }

    private void UploadFilesWithDialog()
    {
        var dialog = new OpenFileDialog { Title = Strings.Upload_PickFilesTitle, Multiselect = true };
        if (dialog.ShowDialog() == true)
        {
            _ = QuickUploader.UploadFilesAsync(dialog.FileNames);
        }
    }

    public string? PickImageFile(Window? owner)
    {
        var dialog = new OpenFileDialog
        {
            Title = Strings.App_OpenImageTitle,
            Filter = Strings.App_OpenImageFilter,
            InitialDirectory = CaptureCoordinator.ScreenshotsFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
        };
        bool? ok = owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        return ok == true ? dialog.FileName : null;
    }

    public void ShowSettings()
    {
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow(Settings);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }

        BringToFront(_settingsWindow);
    }

    /// <summary>Instellingen openen op het tabblad Workflows.</summary>
    public void ShowWorkflowSettings()
    {
        ShowSettings();
        _settingsWindow?.ShowWorkflowsTab();
    }

    /// <summary>Instellingen openen op het tabblad Watermerk.</summary>
    public void ShowWatermarkSettings()
    {
        ShowSettings();
        _settingsWindow?.ShowWatermarkTab();
    }

    public void ShowHistory(bool images)
    {
        if (_historyWindow == null)
        {
            _historyWindow = new HistoryWindow(images);
            _historyWindow.Closed += (_, _) => _historyWindow = null;
            _historyWindow.Show();
        }
        else
        {
            _historyWindow.ShowImages(images);
        }

        BringToFront(_historyWindow);
    }

    /// <summary>Map met knipsels: de opslagmap, anders de laatst gebruikte map, anders Afbeeldingen.</summary>
    public string SnipFolder =>
        ExistingFolder(Settings.SnipSaveFolder, Settings.LastSaveFolder)
        ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

    /// <summary>Map met screencasts: de opslagmap, anders de laatst gebruikte map, anders Video's\kniip (bestaat mogelijk nog niet).</summary>
    public string VideoFolder =>
        ExistingFolder(Settings.VideoSaveFolder, Settings.LastVideoFolder)
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "kniip");

    private static string? ExistingFolder(params string?[] folders) =>
        folders.FirstOrDefault(folder => folder is { Length: > 0 } && Directory.Exists(folder));

    private IReadOnlyList<(string Label, string? Path)> TrayFolders() => new (string, string?)[]
    {
        (Strings.App_FolderScreenshots, ExistingFolder(CaptureCoordinator.ScreenshotsFolder)),
        (Strings.Common_Snips, SnipFolder),
        ("Screencasts", VideoFolder)
    };

    public void ShowBalloon(string title, string text, bool error = false, Action? onClick = null) =>
        _tray?.ShowBalloon(title, text, error, onClick);

    /// <summary>Melding na een upload; klikken opent de link in de browser.</summary>
    public void ShowLinkBalloon(string title, string url) =>
        ShowBalloon(title, string.Format(Strings.Workflow_DoneUrl, ShortUrl(url)), onClick: () => OpenUrl(url));

    /// <summary>Zonder https:// past de link beter in de melding; klikken opent de volledige link.</summary>
    private static string ShortUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? url[(uri.Scheme.Length + 3)..]
            : url;

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowBalloon(AppIdentity.DisplayName, ex.Message, error: true);
        }
    }

    /// <summary>Vervangt alle instellingen (herstel uit backup); daarna herstarten, want vensters houden de oude instantie vast.</summary>
    internal void ReplaceSettings(AppSettings settings)
    {
        Settings = settings;
        SettingsStore.Save(Settings);
    }

    public void SaveSettings()
    {
        SettingsStore.Save(Settings);
        ThemeManager.Apply(Settings.ColorMode);
        Capture.ApplySettings(Settings);
        ApplyHotkeys();
        ApplyWorkflowBar();
        Updates.Apply(Settings.CheckForUpdates, TimeSpan.FromHours(Settings.UpdateCheckHours));
    }

    private static void BringToFront(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Topmost = true;
        window.Activate();
        window.Topmost = false;
        window.Focus();
    }

    private void ExitApplication()
    {
        if (TryCloseAllWindows())
        {
            Shutdown();
        }
    }

    /// <summary>Sluit alle vensters; false als een screencast loopt of een tabblad in de editor open blijft.</summary>
    private bool TryCloseAllWindows()
    {
        if (Recorder.Stage != RecordingStage.Idle)
        {
            MessageBox.Show(Strings.App_ScreencastRunning, "kniip",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        // Ook bij "Tabbladen behouden": afsluiten sluit alles, met de gewone vraag per tabblad.
        if (_editorShell != null && !_editorShell.CloseAll())
        {
            return false;
        }

        // Eerst loskoppelen: de balk blijft dan bij de volgende start open.
        WorkflowBarWindow? bar = _workflowBar;
        _workflowBar = null;
        bar?.Close();
        foreach (Window window in Windows.Cast<Window>().ToList())
        {
            window.Close();
        }

        if (_editorShell != null)
        {
            ApplyWorkflowBar();
            return false;
        }

        return true;
    }

    /// <summary>Sluit kniip en start het opnieuw met de instellingen open, bijvoorbeeld na het wijzigen van de taal.</summary>
    public void Restart()
    {
        if (!TryCloseAllWindows() || Environment.ProcessPath is not { } exe)
        {
            return;
        }

        // De nieuwe instantie moet de mutex kunnen krijgen, dus die nu al vrijgeven; OnExit slaat dan niet meer op.
        SettingsStore.Save(Settings);
        _singleInstance?.Dispose();
        _singleInstance = null;
        try
        {
            Process.Start(new ProcessStartInfo(exe, "--settings") { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            MessageBox.Show(string.Format(Strings.App_RestartFailed, ex.Message), "kniip",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        Shutdown();
    }

    /// <summary>Start de gedownloade installer en sluit kniip; de installer start kniip daarna opnieuw.</summary>
    public void InstallUpdate(string installerPath)
    {
        if (!TryCloseAllWindows())
        {
            return;
        }

        try
        {
            UpdateInstaller.Start(installerPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(string.Format(Strings.App_InstallerFailed, ex.Message), "kniip",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_singleInstance != null)
        {
            SettingsStore.Save(Settings);
        }

        Capture?.Dispose();
        HotkeyHook?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.Folder);
            File.AppendAllText(Path.Combine(SettingsStore.Folder, "error.log"), $"{DateTime.Now:O}\n{e.Exception}\n\n");
        }
        catch (Exception)
        {
            // Loggen is best effort.
        }

        MessageBox.Show(string.Format(Strings.App_UnhandledError, e.Exception.Message), "kniip", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
