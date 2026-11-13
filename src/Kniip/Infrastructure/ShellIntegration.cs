using System;
using Kniip.Interop;
using Microsoft.Win32;
using Kniip.Resources;

namespace Kniip.Infrastructure;

/// <summary>
/// Registratie per gebruiker (HKCU, geen beheerdersrechten nodig) voor "Openen met" en autostart.
/// </summary>
internal static class ShellIntegration
{
    private const string ProgId = AppIdentity.Key + ".Image";
    private const string AppKeyName = AppIdentity.Key + ".exe";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = AppIdentity.Key;

    public static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };

    private static string ExePath => Environment.ProcessPath
        ?? throw new InvalidOperationException(Strings.Shell_NoAppPath);

    private static string OpenCommand => $"\"{ExePath}\" \"%1\"";

    public static bool IsOpenWithRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ProgId}\shell\open\command");
        return key?.GetValue(null) is string command
               && string.Equals(command, OpenCommand, StringComparison.OrdinalIgnoreCase);
    }

    public static void RegisterOpenWith()
    {
        using (var prog = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
        {
            prog.SetValue(null, string.Format(Strings.Shell_ImageType, AppIdentity.DisplayName));
            prog.SetValue("FriendlyTypeName", string.Format(Strings.Shell_ImageType, AppIdentity.DisplayName));
        }
        using (var icon = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}\DefaultIcon"))
        {
            icon.SetValue(null, $"\"{ExePath}\",0");
        }
        using (var cmd = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}\shell\open\command"))
        {
            cmd.SetValue(null, OpenCommand);
        }

        using (var app = Registry.CurrentUser.CreateSubKey($@"Software\Classes\Applications\{AppKeyName}"))
        {
            app.SetValue("FriendlyAppName", $"{AppIdentity.DisplayName} editor");
        }
        using (var appCmd = Registry.CurrentUser.CreateSubKey($@"Software\Classes\Applications\{AppKeyName}\shell\open\command"))
        {
            appCmd.SetValue(null, OpenCommand);
        }
        using (var types = Registry.CurrentUser.CreateSubKey($@"Software\Classes\Applications\{AppKeyName}\SupportedTypes"))
        {
            foreach (string ext in ImageExtensions)
            {
                types.SetValue(ext, string.Empty);
            }
        }

        foreach (string ext in ImageExtensions)
        {
            using var openWith = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ext}\OpenWithProgids");
            openWith.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
        }

        NotifyShell();
    }

    public static void UnregisterOpenWith()
    {
        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ProgId}", false);
        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\Applications\{AppKeyName}", false);

        foreach (string ext in ImageExtensions)
        {
            using var openWith = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ext}\OpenWithProgids", true);
            openWith?.DeleteValue(ProgId, false);
        }

        NotifyShell();
    }

    /// <summary>
    /// Zet de registratie van voor de hernoeming (Knip+ / KnipPlus.exe) om naar kniip,
    /// zodat autostart en "Openen met" niet naar een verdwenen exe blijven wijzen.
    /// </summary>
    public static void MigrateLegacyRegistration()
    {
        if (AppIdentity.IsTest)
        {
            return;
        }

        const string legacyProgId = "KnipPlus.Image";
        const string legacyAppKeyName = "KnipPlus.exe";
        const string legacyRunValueName = "KnipPlus";

        bool hadAutostart;
        using (var run = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
        {
            hadAutostart = run?.GetValue(legacyRunValueName) is string;
            run?.DeleteValue(legacyRunValueName, false);
        }

        bool hadOpenWith;
        using (var prog = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{legacyProgId}"))
        {
            hadOpenWith = prog != null;
        }

        if (hadOpenWith)
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{legacyProgId}", false);
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\Applications\{legacyAppKeyName}", false);
            foreach (string ext in ImageExtensions)
            {
                using var openWith = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ext}\OpenWithProgids", true);
                openWith?.DeleteValue(legacyProgId, false);
            }
        }

        if (hadAutostart)
        {
            SetAutostart(true);
        }

        if (hadOpenWith)
        {
            RegisterOpenWith();
        }
    }

    public static bool IsAutostartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(RunValueName) is string;
    }

    public static void SetAutostart(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
        {
            key.SetValue(RunValueName, $"\"{ExePath}\" --tray");
        }
        else
        {
            key.DeleteValue(RunValueName, false);
        }
    }

    private static void NotifyShell() =>
        NativeMethods.SHChangeNotify(NativeMethods.SHCNE_ASSOCCHANGED, 0, IntPtr.Zero, IntPtr.Zero);
}
