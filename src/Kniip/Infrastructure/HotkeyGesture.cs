using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace Kniip.Infrastructure;

/// <summary>
/// Toetscombinatie voor de toetsenbordhaak. Anders dan bij <see cref="KeyGesture"/> mag Print Screen
/// zonder Ctrl/Alt/Shift/Win. Opgeslagen als tekst, bijvoorbeeld "PrintScreen" of "Ctrl+Shift+S".
/// </summary>
internal readonly record struct HotkeyGesture(ModifierKeys Modifiers, Key Key)
{
    /// <summary>Gedeelde enumwaarden (Snapshot = PrintScreen, Return = Enter, ...) krijgen één vaste naam.</summary>
    private static readonly Dictionary<Key, (string Stored, string Display)> Names = new()
    {
        [Key.PrintScreen] = ("PrintScreen", "Print Screen"),
        [Key.Enter] = ("Enter", "Enter"),
        [Key.CapsLock] = ("CapsLock", "Caps Lock"),
        [Key.PageUp] = ("PageUp", "Page Up"),
        [Key.PageDown] = ("PageDown", "Page Down"),
        [Key.Scroll] = ("Scroll", "Scroll Lock"),
        [Key.Back] = ("Back", "Backspace"),
    };

    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string[] parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        ModifierKeys modifiers = ModifierKeys.None;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= ModifierKeys.Control;
                    break;
                case "alt":
                    modifiers |= ModifierKeys.Alt;
                    break;
                case "shift":
                    modifiers |= ModifierKeys.Shift;
                    break;
                case "win":
                case "windows":
                    modifiers |= ModifierKeys.Windows;
                    break;
                default:
                    return false;
            }
        }

        string keyText = parts[^1].Replace(" ", string.Empty);
        // KeyGesture schrijft cijfertoetsen als "1"; Enum.TryParse zou dat als enumwaarde 1 lezen.
        if (keyText.Length == 1 && char.IsAsciiDigit(keyText[0]))
        {
            keyText = "D" + keyText;
        }

        if (!Enum.TryParse(keyText, true, out Key key) || key == Key.None || IsModifierKey(key))
        {
            return false;
        }

        gesture = new HotkeyGesture(modifiers, key);
        return true;
    }

    public static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin or Key.System;

    /// <summary>
    /// Zonder Ctrl/Alt/Shift/Win alleen toetsen die je niet gebruikt om te typen,
    /// anders zou de haak die toets in alle programma's opeten.
    /// </summary>
    public bool IsAllowed =>
        Modifiers is not (ModifierKeys.None or ModifierKeys.Shift)
        || Key is Key.PrintScreen or Key.Pause or Key.Scroll or (>= Key.F1 and <= Key.F24);

    public override string ToString() => Join("+", Names.TryGetValue(Key, out var name) ? name.Stored : Key.ToString());

    public string DisplayText => Join(" + ", Names.TryGetValue(Key, out var name) ? name.Display : DisplayKey(Key));

    private string Join(string separator, string key)
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key);
        return string.Join(separator, parts);
    }

    private static string DisplayKey(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => $"Num {key - Key.NumPad0}",
        _ => key.ToString()
    };
}
