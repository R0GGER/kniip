using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Input;
using System.Windows.Threading;
using Kniip.Interop;

namespace Kniip.Infrastructure;

/// <summary>
/// Systeembrede sneltoets via een low-level toetsenbordhaak. Anders dan RegisterHotKey werkt dit ook
/// voor Print Screen, dat Windows zelf vaak al heeft vastgelegd. De haak draait op een eigen thread,
/// zodat een drukke UI-thread het typen in andere programma's niet vertraagt.
/// </summary>
internal sealed class GlobalHotkeyHook : IDisposable
{
    /// <summary>Niet-toegewezen virtuele toets om het Startmenu (Win) of de menubalk (Alt) te onderdrukken.</summary>
    private const byte MaskKey = 0xE8;

    private readonly Dispatcher _dispatcher;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private readonly NativeMethods.LowLevelKeyboardProc _proc;
    private uint _threadId;
    private IntPtr _hook;

    private readonly object _gate = new();
    private IReadOnlyDictionary<HotkeyGesture, Action> _bindings = new Dictionary<HotkeyGesture, Action>();
    private Action<HotkeyGesture>? _capture;
    private int _heldVk;

    public GlobalHotkeyHook(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _proc = HookProc;
        _thread = new Thread(Run) { IsBackground = true, Name = "kniip toetsenbordhaak" };
        _thread.Start();
        _ready.Wait();
    }

    public bool IsActive => _hook != IntPtr.Zero;

    /// <summary>Sneltoetsen en de actie die ze op de UI-thread starten.</summary>
    public IReadOnlyDictionary<HotkeyGesture, Action> Bindings
    {
        get { lock (_gate) return _bindings; }
        set { lock (_gate) _bindings = value; }
    }

    /// <summary>
    /// Zolang dit gezet is, gaat elke toets (behalve losse Ctrl/Alt/Shift/Win) naar deze handler
    /// op de UI-thread in plaats van naar het actieve programma, en start de sneltoets niets.
    /// </summary>
    public Action<HotkeyGesture>? Capture
    {
        get { lock (_gate) return _capture; }
        set { lock (_gate) _capture = value; }
    }

    private void Run()
    {
        _threadId = NativeMethods.GetCurrentThreadId();
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _proc, NativeMethods.GetModuleHandle(null), 0);
        _ready.Set();
        if (_hook == IntPtr.Zero)
        {
            return;
        }

        while (NativeMethods.GetMessage(out NativeMethods.MSG _, IntPtr.Zero, 0, 0) > 0)
        {
        }

        NativeMethods.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && Handle(wParam.ToInt32(), Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam)))
        {
            return new IntPtr(1);
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    /// <returns>True als de toets ingeslikt moet worden.</returns>
    private bool Handle(int message, NativeMethods.KBDLLHOOKSTRUCT data)
    {
        int vk = (int)data.vkCode;
        if (vk == MaskKey)
        {
            return false;
        }

        if (message is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP)
        {
            if (vk != _heldVk)
            {
                return false;
            }

            _heldVk = 0;
            return true;
        }

        if (message is not (NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN))
        {
            return false;
        }

        if (vk == _heldVk)
        {
            // Automatische herhaling bij ingedrukt houden: inslikken, maar niet opnieuw starten.
            return true;
        }

        Key key = KeyInterop.KeyFromVirtualKey(vk);
        if (key == Key.None || HotkeyGesture.IsModifierKey(key))
        {
            return false;
        }

        var pressed = new HotkeyGesture(CurrentModifiers(), key);
        Action<HotkeyGesture>? capture;
        IReadOnlyDictionary<HotkeyGesture, Action> bindings;
        lock (_gate)
        {
            capture = _capture;
            bindings = _bindings;
        }

        if (capture != null)
        {
            _dispatcher.BeginInvoke(() => capture(pressed));
        }
        else if (bindings.TryGetValue(pressed, out Action? action))
        {
            _dispatcher.BeginInvoke(action);
        }
        else
        {
            return false;
        }

        _heldVk = vk;
        if ((pressed.Modifiers & (ModifierKeys.Alt | ModifierKeys.Windows)) != 0)
        {
            NativeMethods.keybd_event(MaskKey, 0, 0, UIntPtr.Zero);
            NativeMethods.keybd_event(MaskKey, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        return true;
    }

    private static ModifierKeys CurrentModifiers()
    {
        ModifierKeys modifiers = ModifierKeys.None;
        if (IsDown(NativeMethods.VK_CONTROL)) modifiers |= ModifierKeys.Control;
        if (IsDown(NativeMethods.VK_MENU)) modifiers |= ModifierKeys.Alt;
        if (IsDown(NativeMethods.VK_SHIFT)) modifiers |= ModifierKeys.Shift;
        if (IsDown(NativeMethods.VK_LWIN) || IsDown(NativeMethods.VK_RWIN)) modifiers |= ModifierKeys.Windows;
        return modifiers;
    }

    private static bool IsDown(int vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

    public void Dispose()
    {
        if (_threadId != 0)
        {
            NativeMethods.PostThreadMessage(_threadId, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            _thread.Join(TimeSpan.FromSeconds(1));
        }

        _ready.Dispose();
    }
}
