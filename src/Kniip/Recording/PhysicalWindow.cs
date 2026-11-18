using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Kniip.Interop;
using Kniip.Settings;

namespace Kniip.Recording;

/// <summary>Hulpfuncties om WPF-vensters in fysieke pixels te plaatsen (per-monitor DPI v2).</summary>
internal static class PhysicalWindow
{
    public static IntPtr Handle(Window window) => new WindowInteropHelper(window).EnsureHandle();

    public static double Scale(Visual visual) => VisualTreeHelper.GetDpi(visual).DpiScaleX;

    /// <summary>
    /// Voor schermvullende overlays met een vastgelegd scherm: met de GPU houdt de videodriver per overlay
    /// kopieën van het beeld vast die na sluiten niet vrijkomen (honderden MB na een paar knipsels).
    /// </summary>
    public static void UseSoftwareRendering(Window window)
    {
        if (HwndSource.FromHwnd(Handle(window))?.CompositionTarget is HwndTarget target)
        {
            target.RenderMode = RenderMode.SoftwareOnly;
        }
    }

    /// <summary>
    /// Twee keer plaatsen: de eerste keer kan het venster naar een scherm met andere DPI springen,
    /// waarna WPF de grootte aanpast; de tweede keer zet de juiste fysieke grootte terug.
    /// </summary>
    public static void Place(Window window, Rectangle bounds, bool resize = true)
    {
        IntPtr hwnd = Handle(window);
        uint flags = NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | (resize ? 0 : NativeMethods.SWP_NOSIZE);
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height, flags);
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height, flags);
    }

    public static Rectangle GetBounds(Window window)
    {
        NativeMethods.GetWindowRect(Handle(window), out NativeMethods.RECT rect);
        return new Rectangle(rect.Left, rect.Top, rect.Width, rect.Height);
    }

    /// <summary>Venster onzichtbaar maken voor schermopnames (Windows 10 2004 en nieuwer).</summary>
    public static void ExcludeFromCapture(Window window) =>
        NativeMethods.SetWindowDisplayAffinity(Handle(window), NativeMethods.WDA_EXCLUDEFROMCAPTURE);

    /// <summary>Zichtbare topniveauvensters, van boven naar onder; die van kniip zelf alleen met <paramref name="includeOwn"/>.</summary>
    public static List<Rectangle> SnapshotWindows(bool includeOwn = false) =>
        Snapshot(includeOwn).Select(w => w.Bounds).ToList();

    /// <summary>Programmanaam en titel van het bovenste venster uit <paramref name="windows"/> onder het midden van <paramref name="area"/>.</summary>
    public static WindowInfo? WindowAt(IEnumerable<SnappedWindow> windows, Rectangle area)
    {
        var point = new System.Drawing.Point(area.X + area.Width / 2, area.Y + area.Height / 2);
        foreach (SnappedWindow window in windows)
        {
            if (!window.Bounds.Contains(point))
            {
                continue;
            }

            try
            {
                using Process process = Process.GetProcessById((int)window.ProcessId);
                var title = new StringBuilder(512);
                NativeMethods.GetWindowText(window.Handle, title, title.Capacity);
                return new WindowInfo(process.ProcessName, title.Length > 0 ? title.ToString() : process.ProcessName);
            }
            catch (Exception)
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>Hoekstraal van het venster dat precies <paramref name="area"/> beslaat; 0 als het afgesneden is of niet gevonden.</summary>
    public static int CornerRadiusAt(IEnumerable<SnappedWindow> windows, Rectangle area)
    {
        var point = new System.Drawing.Point(area.X + area.Width / 2, area.Y + area.Height / 2);
        SnappedWindow? window = windows.FirstOrDefault(w => w.Bounds.Contains(point));
        return window != null && window.Bounds == area ? NativeMethods.GetWindowCornerRadius(window.Handle) : 0;
    }

    public static List<SnappedWindow> Snapshot(bool includeOwn = false)
    {
        var result = new List<SnappedWindow>();
        int ownPid = Environment.ProcessId;
        var className = new StringBuilder(64);
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd) || NativeMethods.IsCloaked(hwnd))
            {
                return true;
            }

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            long exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            if ((pid == ownPid && !includeOwn) || (exStyle & NativeMethods.WS_EX_TRANSPARENT) != 0)
            {
                return true;
            }

            className.Clear();
            NativeMethods.GetClassName(hwnd, className, className.Capacity);
            string name = className.ToString();
            if (name is "Progman" or "WorkerW")
            {
                return true;
            }

            NativeMethods.RECT rect = NativeMethods.GetVisibleWindowRect(hwnd);
            if (rect.Width > 8 && rect.Height > 8)
            {
                result.Add(new SnappedWindow(new Rectangle(rect.Left, rect.Top, rect.Width, rect.Height), pid, hwnd));
            }

            return true;
        }, IntPtr.Zero);

        return result;
    }
}

internal sealed record SnappedWindow(Rectangle Bounds, uint ProcessId, IntPtr Handle);
