using System;
using System.Windows;
using System.Windows.Interop;
using Kniip.Interop;
using Kniip.Settings;
using Microsoft.Win32;

namespace Kniip.Infrastructure;

/// <summary>Wisselt de kleuren van alle vensters tussen licht en donker (Themes/Light.xaml en Themes/Dark.xaml).</summary>
internal static class ThemeManager
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static readonly Uri LightUri = new("pack://application:,,,/Themes/Light.xaml");
    private static readonly Uri DarkUri = new("pack://application:,,,/Themes/Dark.xaml");

    private static ResourceDictionary? _light;
    private static ResourceDictionary? _dark;
    private static ColorMode _mode;
    private static bool _listening;

    public static bool IsDark { get; private set; }

    /// <summary>Na elke wissel tussen licht en donker.</summary>
    public static event Action? Changed;

    public static void Apply(ColorMode mode)
    {
        _mode = mode;
        if (mode == ColorMode.System && !_listening)
        {
            _listening = true;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }

        Refresh();
    }

    /// <summary>Geeft het venster een titelbalk in de huidige kleurmodus, ook na een latere wissel.</summary>
    public static void Attach(Window window) =>
        window.SourceInitialized += (_, _) => NativeMethods.SetDarkTitleBar(new WindowInteropHelper(window).Handle, IsDark);

    private static void Refresh()
    {
        bool dark = _mode switch
        {
            ColorMode.Dark => true,
            ColorMode.System => IsSystemDark(),
            _ => false
        };
        if (dark == IsDark && _light != null)
        {
            return;
        }

        IsDark = dark;
        _light ??= new ResourceDictionary { Source = LightUri };
        _dark ??= new ResourceDictionary { Source = DarkUri };

        var merged = Application.Current.Resources.MergedDictionaries;
        int index = -1;
        for (int i = 0; i < merged.Count; i++)
        {
            if (merged[i].Source is { } source && (source.OriginalString.EndsWith("Light.xaml", StringComparison.OrdinalIgnoreCase)
                                                   || source.OriginalString.EndsWith("Dark.xaml", StringComparison.OrdinalIgnoreCase)))
            {
                index = i;
                break;
            }
        }

        ResourceDictionary theme = dark ? _dark : _light;
        if (index < 0)
        {
            merged.Insert(0, theme);
        }
        else if (merged[index] != theme)
        {
            merged[index] = theme;
        }

        foreach (Window window in Application.Current.Windows)
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd != IntPtr.Zero && window.WindowStyle != WindowStyle.None)
            {
                NativeMethods.SetDarkTitleBar(hwnd, dark);
            }
        }

        Changed?.Invoke();
    }

    private static void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General)
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (_mode == ColorMode.System)
                {
                    Refresh();
                }
            });
        }
    }

    private static bool IsSystemDark()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
