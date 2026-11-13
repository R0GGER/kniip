using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Rectangle = System.Drawing.Rectangle;
using Kniip.Resources;

namespace Kniip.Recording;

internal enum ToolbarState
{
    Ready,
    Countdown,
    Recording,
    Paused,
    Finishing
}

/// <summary>Knoppenbalk naast het opnamegebied; hij is zelf onzichtbaar in de opname.</summary>
public partial class RecordingToolbar : Window
{
    private readonly Rectangle _region;
    private readonly Rectangle _workArea;
    private readonly Stopwatch _elapsed = new();
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(250) };

    public event Action? RecordRequested;
    public event Action? PauseRequested;
    public event Action? StopRequested;
    public event Action? CancelRequested;

    internal RecordingToolbar(Rectangle region, Rectangle workArea, bool systemAudio, bool microphone)
    {
        InitializeComponent();
        _region = region;
        _workArea = workArea;
        SystemAudioToggle.IsChecked = systemAudio;
        MicrophoneToggle.IsChecked = microphone;

        _clock.Tick += (_, _) => UpdateClock();
        SourceInitialized += (_, _) =>
        {
            PhysicalWindow.ExcludeFromCapture(this);
            PhysicalWindow.Place(this, new Rectangle(region.X, region.Y, 0, 0), resize: false);
        };
        ContentRendered += (_, _) => PlaceNearRegion();
        Closed += (_, _) => _clock.Stop();
    }

    public bool SystemAudio => SystemAudioToggle.IsChecked == true;

    public bool Microphone => MicrophoneToggle.IsChecked == true;

    internal void SetState(ToolbarState state)
    {
        bool active = state is ToolbarState.Recording or ToolbarState.Paused;
        SystemAudioToggle.Visibility = Show(state is ToolbarState.Ready or ToolbarState.Countdown);
        MicrophoneToggle.Visibility = SystemAudioToggle.Visibility;
        RecordButton.Visibility = Show(state is ToolbarState.Ready or ToolbarState.Countdown);
        RecordButton.ToolTip = state == ToolbarState.Countdown ? Strings.Recording_StartNow : Strings.Recording_Start;
        PauseButton.Visibility = Show(active);
        StopButton.Visibility = Show(active);
        TimePanel.Visibility = Show(active || state == ToolbarState.Finishing);
        BusyText.Visibility = Show(state == ToolbarState.Finishing);
        CancelButton.Visibility = Show(state != ToolbarState.Finishing);

        PauseIcon.Text = state == ToolbarState.Paused ? "\uE768" : "\uE769";
        PauseButton.ToolTip = state == ToolbarState.Paused ? Strings.Recording_Resume : Strings.Recording_Pause;
        RecordDot.Opacity = state == ToolbarState.Recording ? 1 : 0.35;

        if (state == ToolbarState.Ready)
        {
            RecordButtonDot.Visibility = Visibility.Visible;
            RecordButtonText.Text = Strings.Recording_Record;
        }

        if (state == ToolbarState.Recording)
        {
            _elapsed.Start();
            _clock.Start();
        }
        else
        {
            _elapsed.Stop();
        }

        UpdateClock();
    }

    internal void SetCountdown(int seconds)
    {
        RecordButtonDot.Visibility = Visibility.Collapsed;
        RecordButtonText.Text = string.Format(Strings.Recording_StartsIn, seconds);
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private void UpdateClock()
    {
        TimeSpan time = _elapsed.Elapsed;
        TimeText.Text = time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
    }

    /// <summary>Onder het gebied als daar plaats is, anders erboven, anders binnen de onderrand.</summary>
    private void PlaceNearRegion()
    {
        Rectangle own = PhysicalWindow.GetBounds(this);
        int x = _region.X + (_region.Width - own.Width) / 2;
        x = Math.Clamp(x, _workArea.Left, Math.Max(_workArea.Left, _workArea.Right - own.Width));

        int y;
        if (_region.Bottom + own.Height <= _workArea.Bottom)
        {
            y = _region.Bottom;
        }
        else if (_region.Top - own.Height >= _workArea.Top)
        {
            y = _region.Top - own.Height;
        }
        else
        {
            y = Math.Max(_workArea.Top, _region.Bottom - own.Height);
        }

        PhysicalWindow.Place(this, new Rectangle(x, y, own.Width, own.Height), resize: false);
    }

    private void Background_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Record_Click(object sender, RoutedEventArgs e) => RecordRequested?.Invoke();

    private void Pause_Click(object sender, RoutedEventArgs e) => PauseRequested?.Invoke();

    private void Stop_Click(object sender, RoutedEventArgs e) => StopRequested?.Invoke();

    private void Cancel_Click(object sender, RoutedEventArgs e) => CancelRequested?.Invoke();
}
