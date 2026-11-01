using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FullTilt;

internal sealed class WholeNumbers : IValueConverter
{
    internal static readonly WholeNumbers Converter = new();
    internal static double Round(double value) => Math.Round(value, MidpointRounding.AwayFromZero);
    internal static string Format(double value) => Round(value).ToString("0", CultureInfo.CurrentCulture);
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Round(System.Convert.ToDouble(value, culture));
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        double.TryParse(value?.ToString(), NumberStyles.Float, culture, out var number) && double.IsFinite(number) ? Round(number) : DependencyProperty.UnsetValue;
}
