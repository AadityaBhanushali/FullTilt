using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System;
using System.Globalization;

namespace FullTilt;

// Theme-bound translucent materials. Text/icons remain sharp above these surfaces.
internal static class PieGlass
{
    internal const double DesktopDarkening = .30;
    // Equivalent to a 30% black veil over the old material, folded into one
    // translucent brush so text/icons stay untouched and theme updates still bind.
    private sealed class DarkenGlass : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var color = (Color)value;
            if (!ThemePalette.IsDark) return color;
            var alpha = color.A / 255d;
            var darkerAlpha = alpha + DesktopDarkening * (1 - alpha);
            var tint = alpha * (1 - DesktopDarkening) / darkerAlpha;
            return Color.FromArgb((byte)Math.Round(darkerAlpha * 255), (byte)Math.Round(color.R * tint), (byte)Math.Round(color.G * tint), (byte)Math.Round(color.B * tint));
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
    private static readonly DarkenGlass Darkening = new();
    public static Brush DesktopSurface(bool selected = false)
    {
        if (selected) return Surface(true);
        var original = (LinearGradientBrush)Surface();
        var darker = new LinearGradientBrush { StartPoint = original.StartPoint, EndPoint = original.EndPoint };
        foreach (var stop in original.GradientStops)
        {
            var adjusted = new GradientStop { Offset = stop.Offset };
            BindingOperations.SetBinding(adjusted, GradientStop.ColorProperty, new Binding(nameof(GradientStop.Color)) { Source = stop, Converter = Darkening });
            darker.GradientStops.Add(adjusted);
        }
        return darker;
    }
    private static GradientStop Stop(string color, double offset)
    {
        var stop = new GradientStop { Offset = offset };
        BindingOperations.SetBinding(stop, GradientStop.ColorProperty,
            new Binding(nameof(SolidColorBrush.Color)) { Source = ThemePalette.Get(color) });
        return stop;
    }
    public static Brush Surface(bool selected = false)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(.8, 1) };
        brush.GradientStops.Add(Stop(selected ? "#E8DBFFA4" : "#B8D9DFE3", 0));
        brush.GradientStops.Add(Stop(selected ? "#B8C8F782" : "#90BFC9D0", .65));
        brush.GradientStops.Add(Stop(selected ? "#E8DBFFA4" : "#B8D9DFE3", 1));
        return brush;
    }
    public static Brush Edge()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        brush.GradientStops.Add(Stop("#E8FFFFFF", 0));
        brush.GradientStops.Add(Stop("#28FFFFFF", .55));
        brush.GradientStops.Add(Stop("#60FFFFFF", 1));
        return brush;
    }
    public static Brush Sheen()
    {
        var brush = new RadialGradientBrush { Center = new Point(.22, .06), GradientOrigin = new Point(.22, .06), RadiusX = .8, RadiusY = .9 };
        brush.GradientStops.Add(Stop("#60FFFFFF", 0));
        brush.GradientStops.Add(Stop("#28FFFFFF", .35));
        brush.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        return brush;
    }
}
