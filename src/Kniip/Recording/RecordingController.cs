using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Kniip.Interop;
using Kniip.Settings;
using Screen = System.Windows.Forms.Screen;
using Kniip.Resources;

namespace Kniip.Recording;

internal enum RecordingStage
{
    Idle,
    Selecting,
    Ready,
    Countdown,
    Recording,
    Paused,
    Finishing
}

/// <summary>Stuurt een screencast aan: gebied kiezen, knoppenbalk, aftellen, opnemen en afronden.</summary>
internal sealed class RecordingController
{
    private const int CountdownSeconds = 3;

    private readonly AppSettings _settings;
    private readonly Dispatcher _dispatcher;
    private readonly List<RegionSelectorWindow> _selectors = new();
    private readonly DispatcherTimer _countdownTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private RegionFrameWindow? _frame;
    private RecordingToolbar? _toolbar;
    private RecordingSession? _session;
    private RecordingRegion? _region;
    private int _countdown;
    private bool _discard;
    private bool _retriedWithoutAudio;

    public RecordingController(AppSettings settings, Dispatcher dispatcher)
    {
        _settings = settings;
        _dispatcher = dispatcher;
        _countdownTimer.Tick += (_, _) => CountdownTick();
    }

    public RecordingStage Stage { get; private set; }

    /// <summary>Formaat dat de nabewerking voorstelt; null = het laatst gebruikte.</summary>
    private ScreencastFormat? _format;

    public event Action<RecordingStage>? StageChanged;

    /// <summary>Pad van een geslaagde opname (tijdelijk MP4-bestand), het voorgestelde formaat en de programmanaam van het opgenomen venster.</summary>
    public event Action<string, ScreencastFormat?, string?>? RecordingFinished;

    private List<SnappedWindow> _snapshot = new();
    private string? _windowName;

    public event Action<string, string>? Notify;

    /// <summary>Sneltoets: kiezen → starten → stoppen. Het formaat telt alleen bij het begin.</summary>
    public void Toggle(ScreencastFormat format)
    {
        if (Stage == RecordingStage.Idle)
        {
            BeginSelection(format);
        }
        else
        {
            Toggle();
        }
    }

    public void Toggle()
    {
        switch (Stage)
        {
            case RecordingStage.Idle:
                BeginSelection(null);
                break;
            case RecordingStage.Selecting:
                OnRegionChosen(null);
                break;
            case RecordingStage.Ready:
                StartCountdown();
                break;
            case RecordingStage.Countdown:
                _countdownTimer.Stop();
                StartRecording(CurrentOptions());
                break;
            case RecordingStage.Recording:
            case RecordingStage.Paused:
                Stop();
                break;
        }
    }

    /// <summary>Systeemvak: starten, of een lopende opname stoppen, of de voorbereiding annuleren.</summary>
    public void TrayAction()
    {
        switch (Stage)
        {
            case RecordingStage.Idle:
                BeginSelection(null);
                break;
            case RecordingStage.Recording:
            case RecordingStage.Paused:
                Stop();
                break;
            case RecordingStage.Finishing:
                break;
            default:
                Cancel();
                break;
        }
    }

    private void SetStage(RecordingStage stage)
    {
        Stage = stage;
        StageChanged?.Invoke(stage);
    }

    private void BeginSelection(ScreencastFormat? format)
    {
        _format = format;
        SetStage(RecordingStage.Selecting);
        _snapshot = PhysicalWindow.Snapshot();
        List<System.Drawing.Rectangle> windows = _snapshot.Select(w => w.Bounds).ToList();
        NativeMethods.GetCursorPos(out NativeMethods.POINT cursor);
        var cursorPoint = new System.Drawing.Point(cursor.X, cursor.Y);

        // Vastleggen voordat de overlays er staan, anders zit hun grijze laag in het vergrootglas.
        var frozen = Screen.AllScreens
            .Select(screen => (screen, image: _settings.ShowMagnifier ? Kniip.Capture.ScreenGrabber.Capture(screen.Bounds) : null))
            .ToList();
        foreach ((Screen screen, System.Windows.Media.Imaging.BitmapSource? image) in frozen)
        {
            var selector = new RegionSelectorWindow(screen, windows, image);
            selector.Finished += OnRegionChosen;
            _selectors.Add(selector);
            selector.Show();
        }

        RegionSelectorWindow? active = _selectors.FirstOrDefault(s => s.ContainsCursor(cursorPoint)) ?? _selectors.FirstOrDefault();
        active?.Activate();
        active?.Focus();
    }

    private void OnRegionChosen(RecordingRegion? region)
    {
        foreach (RegionSelectorWindow selector in _selectors)
        {
            selector.Finished -= OnRegionChosen;
            selector.Close();
        }

        _selectors.Clear();
        List<SnappedWindow> snapshot = _snapshot;
        _snapshot = new();
        if (region == null)
        {
            SetStage(RecordingStage.Idle);
            return;
        }

        _region = region;
        _windowName = PhysicalWindow.ProgramNameAt(snapshot, region.Bounds);
        Screen screen = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == region.DeviceName) ?? Screen.PrimaryScreen!;

        _frame = new RegionFrameWindow(region.Bounds);
        _frame.Show();

        _toolbar = new RecordingToolbar(region.Bounds, screen.WorkingArea, _settings.RecordSystemAudio, _settings.RecordMicrophone);
        _toolbar.RecordRequested += Toggle;
        _toolbar.PauseRequested += TogglePause;
        _toolbar.StopRequested += Stop;
        _toolbar.CancelRequested += Cancel;
        _toolbar.SetState(ToolbarState.Ready);
        _toolbar.Show();
        _toolbar.Activate();
        SetStage(RecordingStage.Ready);
    }

    private RecordingOptions CurrentOptions()
    {
        if (_toolbar != null)
        {
            _settings.RecordSystemAudio = _toolbar.SystemAudio;
            _settings.RecordMicrophone = _toolbar.Microphone;
        }

        return new RecordingOptions(_settings.RecordSystemAudio, _settings.RecordMicrophone,
            _settings.RecordShowCursor, _settings.RecordHighlightClicks, Math.Clamp(_settings.RecordFps, 5, 60));
    }

    private void StartCountdown()
    {
        if (Stage != RecordingStage.Ready || _toolbar == null)
        {
            return;
        }

        _countdown = CountdownSeconds;
        _toolbar.SetState(ToolbarState.Countdown);
        _toolbar.SetCountdown(_countdown);
        SetStage(RecordingStage.Countdown);
        _countdownTimer.Start();
    }

    private void CountdownTick()
    {
        _countdown--;
        if (_countdown > 0)
        {
            _toolbar?.SetCountdown(_countdown);
            return;
        }

        _countdownTimer.Stop();
        StartRecording(CurrentOptions());
    }

    private void StartRecording(RecordingOptions options)
    {
        if (_region == null)
        {
            return;
        }

        _session = new RecordingSession(_region, options, _dispatcher);
        _session.Completed += OnCompleted;
        _session.Failed += OnFailed;
        _toolbar?.SetState(ToolbarState.Recording);
        _frame?.SetRecording(true);
        SetStage(RecordingStage.Recording);

        try
        {
            _session.Start();
        }
        catch (Exception ex)
        {
            OnFailed(ex.Message, false);
            return;
        }

        if (_session.MissingAudio != null)
        {
            Notify?.Invoke(string.Format(Strings.Recording_WithoutAudioTitle, _session.MissingAudio),
                string.Format(Strings.Recording_WithoutAudioText, _session.MissingAudio));
        }
    }

    private void TogglePause()
    {
        if (_session == null)
        {
            return;
        }

        if (Stage == RecordingStage.Paused)
        {
            _session.Resume();
            _toolbar?.SetState(ToolbarState.Recording);
            SetStage(RecordingStage.Recording);
        }
        else if (Stage == RecordingStage.Recording)
        {
            _session.Pause();
            _toolbar?.SetState(ToolbarState.Paused);
            SetStage(RecordingStage.Paused);
        }
    }

    private void Stop()
    {
        if (_session == null || Stage is not (RecordingStage.Recording or RecordingStage.Paused))
        {
            return;
        }

        _toolbar?.SetState(ToolbarState.Finishing);
        SetStage(RecordingStage.Finishing);
        _session.Stop();
    }

    private void Cancel()
    {
        _countdownTimer.Stop();
        if (_session != null && Stage is RecordingStage.Recording or RecordingStage.Paused)
        {
            _discard = true;
            Stop();
            return;
        }

        if (Stage == RecordingStage.Selecting)
        {
            OnRegionChosen(null);
            return;
        }

        EndAll();
    }

    private void OnCompleted(string path)
    {
        bool discard = _discard;
        ScreencastFormat? format = _format;
        string? windowName = _windowName;
        EndAll();
        if (discard)
        {
            TryDelete(path);
            return;
        }

        RecordingFinished?.Invoke(path, format, windowName);
    }

    private void OnFailed(string error, bool started)
    {
        RecordingOptions? options = _session?.Options;
        string? path = _session?.OutputPath;
        DisposeSession();

        if (!started && options is { HasAudio: true } && !_retriedWithoutAudio && !_discard)
        {
            _retriedWithoutAudio = true;
            Notify?.Invoke(Strings.Recording_NoSoundTitle, Strings.Recording_NoSoundText);
            StartRecording(options with { SystemAudio = false, Microphone = false });
            return;
        }

        bool discard = _discard;
        EndAll();
        if (path != null)
        {
            TryDelete(path);
        }

        if (!discard)
        {
            MessageBox.Show(string.Format(Strings.Recording_Failed, error), "kniip", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void DisposeSession()
    {
        if (_session == null)
        {
            return;
        }

        _session.Completed -= OnCompleted;
        _session.Failed -= OnFailed;
        RecordingSession session = _session;
        _session = null;
        // Disposen vanuit een callback van de recorder kan vastlopen; daarom even later.
        _dispatcher.BeginInvoke(session.Dispose, DispatcherPriority.Background);
    }

    private void EndAll()
    {
        _countdownTimer.Stop();
        DisposeSession();
        _toolbar?.Close();
        _toolbar = null;
        _frame?.Close();
        _frame = null;
        _region = null;
        _windowName = null;
        _format = null;
        _discard = false;
        _retriedWithoutAudio = false;
        SetStage(RecordingStage.Idle);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // Tijdelijk bestand; Windows ruimt het later op.
        }
    }
}
