using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Kniip.Editor;

/// <summary>Zet een hoekstraal om naar de linkerbovenhoek van het voorbeeldicoon in de hoekenkeuze.</summary>
public sealed class CornerPreviewConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double radius = value is double d ? d : 0;
        return new CornerRadius(Math.Min(14, radius * 0.7), 0, 0, 0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
