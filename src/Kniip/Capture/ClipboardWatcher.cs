using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Kniip.Interop;

namespace Kniip.Capture;

/// <summary>
/// Luistert met een message-only venster naar WM_CLIPBOARDUPDATE en meldt nieuwe afbeeldingen.
/// </summary>
internal sealed class ClipboardWatcher : IDisposable
{
    private static readonly string[] SnippingToolProcesses =
    {
        "SnippingTool", "ScreenClippingHost", "ScreenSketch"
    };

    private readonly HwndSource _source;
    private bool _listening;

    public event Action<BitmapSource>? ImageCaptured;

    public bool Enabled { get; set; }

    public bool OnlyFromSnippingTool { get; set; } = true;

    public ClipboardWatcher()
    {
        var parameters = new HwndSourceParameters("KniipClipboardWatcher")
        {
            ParentWindow = NativeMethods.HWND_MESSAGE,
            WindowStyle = 0
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
        _listening = NativeMethods.AddClipboardFormatListener(_source.Handle);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_CLIPBOARDUPDATE && Enabled)
        {
            OnClipboardChanged();
        }

        return IntPtr.Zero;
    }

    private async void OnClipboardChanged()
    {
        string? owner = GetOwnerProcessName(out bool isOwnProcess);
        if (isOwnProcess)
        {
            return;
        }

        if (OnlyFromSnippingTool && !IsSnippingTool(owner))
        {
            return;
        }

        // Het Knipprogramma vult het klembord soms in stappen; even wachten en een paar keer proberen.
        for (int attempt = 0; attempt < 6; attempt++)
        {
            await Task.Delay(attempt == 0 ? 120 : 200);
            if (ClipboardImageReader.IsOwnExport())
            {
                return;
            }

            BitmapSource? image = ClipboardImageReader.TryRead();
            if (image != null)
            {
                ImageCaptured?.Invoke(image);
                return;
            }
        }
    }

    private static bool IsSnippingTool(string? processName)
    {
        if (processName == null)
        {
            return false;
        }

        foreach (string name in SnippingToolProcesses)
        {
            if (string.Equals(processName, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? GetOwnerProcessName(out bool isOwnProcess)
    {
        isOwnProcess = false;
        IntPtr owner = NativeMethods.GetClipboardOwner();
        if (owner == IntPtr.Zero)
        {
            return null;
        }

        NativeMethods.GetWindowThreadProcessId(owner, out uint pid);
        if (pid == 0)
        {
            return null;
        }

        if (pid == Environment.ProcessId)
        {
            isOwnProcess = true;
            return null;
        }

        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_listening)
        {
            NativeMethods.RemoveClipboardFormatListener(_source.Handle);
            _listening = false;
        }

        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
