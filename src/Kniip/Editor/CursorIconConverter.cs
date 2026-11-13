using System;
using System.Globalization;
using System.Windows.Data;
using Kniip.Editor.Annotations;

namespace Kniip.Editor;

/// <summary>Geeft de vectorvorm van een cursorsoort, voor het icoon in de cursorkeuze.</summary>
public sealed class CursorIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        CursorGeometries.For(value is CursorKind kind ? kind : CursorKind.Arrow);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
