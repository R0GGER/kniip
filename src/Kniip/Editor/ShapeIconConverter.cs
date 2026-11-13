using System;
using System.Globalization;
using System.Windows.Data;
using Kniip.Editor.Annotations;

namespace Kniip.Editor;

/// <summary>Geeft de vectorvorm van een vormsoort, voor het icoon in de vormkeuze en op de werkbalkknop.</summary>
public sealed class ShapeIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        ShapeGeometries.For(value is ShapeKind kind ? kind : ShapeKind.Triangle);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
