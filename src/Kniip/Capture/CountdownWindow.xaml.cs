using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Kniip.Interop;
using Kniip.Recording;
using Rectangle = System.Drawing.Rectangle;
using Screen = System.Windows.Forms.Screen;

namespace Kniip.Capture;

/// <summary>Klein venster bij de muis dat aftelt tot een vertraagd knipsel; neemt nooit de focus over.</summary>
public partial class CountdownWindow : Window
{
    private const int CursorOffset = 20;

    private readonly DispatcherTimer _follow = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly DateTime _end;
    private bool _finished;

    /// <summary>De tijd is om; het venster is dan al gesloten.</summary>
    public event Action? Elapsed;

    /// <summary>Geannuleerd met Esc of een klik; het venster is dan al gesloten.</summary>
    public event Action? Cancelled;

    public CountdownWindow(int seconds)
    {
        InitializeComponent();
        Left = Top = -32000;
        _end = DateTime.UtcNow.AddSeconds(seconds);
        SecondsText.Text = seconds.ToString();
        _follow.Tick += (_, _) => Tick();
        SourceInitialized += (_, _) =>
        {
            NativeMethods.AddExtendedStyle(PhysicalWindow.Handle(this),
                NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);
            PhysicalWindow.ExcludeFromCapture(this);
        };
        ContentRendered += (_, _) => FollowCursor();
        Closed += (_, _) =>
        {
            _follow.Stop();
            if (!_finished)
            {
                _finished = true;
                Cancelled?.Invoke();
            }
        };
    }

    public void Start()
    {
        Show();
        FollowCursor();
        _follow.Start();
    }

    public void Cancel() => Finish(Cancelled);

    private void Tick()
    {
        TimeSpan left = _end - DateTime.UtcNow;
        if (left <= TimeSpan.Zero)
        {
            Finish(Elapsed);
            return;
        }

        SecondsText.Text = ((int)Math.Ceiling(left.TotalSeconds)).ToString();
        FollowCursor();
    }

    private void FollowCursor()
    {
        if (!IsVisible)
        {
            return;
        }

        NativeMethods.GetCursorPos(out NativeMethods.POINT cursor);
        Rectangle area = Screen.FromPoint(new System.Drawing.Point(cursor.X, cursor.Y)).WorkingArea;
        Rectangle size = PhysicalWindow.GetBounds(this);
        int x = cursor.X + CursorOffset, y = cursor.Y + CursorOffset;
        if (x + size.Width > area.Right)
        {
            x = cursor.X - CursorOffset - size.Width;
        }

        if (y + size.Height > area.Bottom)
        {
            y = cursor.Y - CursorOffset - size.Height;
        }

        x = Math.Max(area.Left, x);
        y = Math.Max(area.Top, y);
        if (x != size.X || y != size.Y)
        {
            PhysicalWindow.Place(this, new Rectangle(x, y, size.Width, size.Height), resize: false);
        }
    }

    private void Finish(Action? handler)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        Close();
        handler?.Invoke();
    }

    private void Border_Click(object sender, MouseButtonEventArgs e) => Cancel();
}
