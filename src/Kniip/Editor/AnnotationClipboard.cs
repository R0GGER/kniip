using System;
using System.Runtime.InteropServices;
using System.Windows;
using Kniip.Editor.Annotations;

namespace Kniip.Editor;

/// <summary>
/// Gekopieerde annotatie, gedeeld tussen editorvensters. Op het Windows-klembord staat alleen een markering,
/// zodat iets wat daarna in een ander programma wordt gekopieerd bij plakken voorgaat.
/// </summary>
internal static class AnnotationClipboard
{
    private const string MarkerFormat = "Kniip.Annotation";

    private static Annotation? _item;
    private static string? _token;

    public static bool Set(Annotation item)
    {
        string token = Guid.NewGuid().ToString("N");
        if (!ClipboardWriter.SetMarker(MarkerFormat, token))
        {
            return false;
        }

        _item = item.Clone();
        _token = token;
        return true;
    }

    /// <summary>Een kopie van de annotatie, of null als het klembord inmiddels iets anders bevat.</summary>
    public static Annotation? Get()
    {
        if (_item == null)
        {
            return null;
        }

        try
        {
            return Clipboard.GetData(MarkerFormat) as string == _token ? _item.Clone() : null;
        }
        catch (ExternalException)
        {
            return null;
        }
    }
}
