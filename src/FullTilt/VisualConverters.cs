using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FullTilt;

public sealed class ComboLabelConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var value = values[0];
        if (value is null || value == DependencyProperty.UnsetValue) return "";
        if (values[1] is string member && member.Length > 0)
            value = TypeDescriptor.GetProperties(value)[member]?.GetValue(value) ?? value;
        return values[2] is string format && format.Length > 0 ? string.Format(culture, format, value) : value.ToString() ?? "";
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
