using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Kniip.Editor;
using Kniip.Infrastructure;
using Kniip.Resources;

namespace Kniip.History;

/// <param name="Glyph">Teken uit Segoe Fluent Icons.</param>
public sealed record HistoryCommand(string Label, string Glyph, bool Enabled, Action Run);

/// <summary>Wat je met een item uit de geschiedenis kunt doen, gedeeld door het systeemvakmenu en het geschiedenisvenster.</summary>
public static class HistoryActions
{
    /// <summary>De acties voor een item; null staat voor een scheidingslijn.</summary>
    public static IReadOnlyList<HistoryCommand?> CommandsFor(HistoryEntry entry)
    {
        bool file = entry.FileExists;
        bool url = entry.Url is { Length: > 0 };
        bool image = file && entry.Kind == HistoryKind.Snip;
        return new HistoryCommand?[]
        {
            new(Strings.History_Open, "\uE8E5", file || url, () => Open(entry)),
            new(Strings.History_OpenInEditor, "\uE70F", image, () => App.Instance.OpenFile(entry.FilePath!)),
            new(Strings.History_OpenLink, "\uE774", url, () => Launch(entry.Url!)),
            null,
            new(Strings.History_CopyLink, "\uE71B", url, () => Copy(entry.Url!)),
            new(Strings.History_CopyImage, "\uE8C8", image, () => CopyImage(entry.FilePath!)),
            new(Strings.History_CopyPath, "\uE8C8", file, () => Copy(entry.FilePath!)),
            new(Strings.History_ShowInFolder, "\uE838", file, () => ShowInFolder(entry.FilePath!))
        };
    }

    /// <summary>Bestand in het standaardprogramma, anders de link in de browser.</summary>
    public static void Open(HistoryEntry entry)
    {
        if (entry.FileExists)
        {
            Launch(entry.FilePath!);
        }
        else if (entry.Url is { Length: > 0 } url)
        {
            Launch(url);
        }
        else
        {
            MessageBox.Show(string.Format(Strings.History_FileMissing, entry.FilePath), "kniip", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    public static void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        Launch(folder);
    }

    private static void ShowInFolder(string path) =>
        Run(() => Process.Start("explorer.exe", $"/select,\"{path}\""));

    private static void Launch(string target) =>
        Run(() => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }));

    private static void Copy(string text)
    {
        if (!ClipboardWriter.SetText(text))
        {
            ClipboardBusy();
        }
    }

    private static void CopyImage(string path)
    {
        try
        {
            if (!ClipboardWriter.SetImage(PixelUtil.LoadFile(path), path))
            {
                ClipboardBusy();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(string.Format(Strings.History_CannotCopy, Path.GetFileName(path), ex.Message), "kniip", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void ClipboardBusy() =>
        MessageBox.Show(Strings.Common_CopyFailedBusyLong, "kniip", MessageBoxButton.OK, MessageBoxImage.Warning);

    private static void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            MessageBox.Show(string.Format(Strings.History_OpenFailed, ex.Message), "kniip", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
