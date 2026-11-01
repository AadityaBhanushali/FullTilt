using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Data;
using System.ComponentModel;

namespace FullTilt;

internal static class ThemePalette
{
    private static readonly Dictionary<string, SolidColorBrush> Brushes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, PaletteColor> Colors = new(StringComparer.OrdinalIgnoreCase);
    private sealed class PaletteColor : INotifyPropertyChanged
    {
        public Color Color { get; private set; }
        public void Set(Color color) { Color = color; PropertyChanged?.Invoke(this, new(nameof(Color))); }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
    private static bool _dark;
    internal static bool IsDark => _dark;
    public static SolidColorBrush Get(string light)
    {
        light = light.ToUpperInvariant();
        if (!Brushes.TryGetValue(light, out var brush))
        {
            var color = new PaletteColor(); color.Set(ColorFor(light)); Colors.Add(light, color);
            brush = new SolidColorBrush();
            // A bound color prevents WPF from freezing brushes captured by templates or drawings.
            BindingOperations.SetBinding(brush, SolidColorBrush.ColorProperty, new Binding(nameof(PaletteColor.Color)) { Source = color });
            Brushes.Add(light, brush);
        }
        return brush;
    }
    private static Color ColorFor(string light)
    {
        var dark = light switch
        {
            "#F9E2E7" => "#51343B",
            "#C54F65" => "#F28F9F",
            "#B8D9DFE3" => "#B0323437",
            "#90BFC9D0" => "#90303235",
            "#E8FFFFFF" => "#7AFFFFFF",
            "#60FFFFFF" => "#25FFFFFF",
            "#28FFFFFF" => "#14FFFFFF",
            "#E8DBFFA4" => "#EECCF768",
            "#B8C8F782" => "#D0AADA4D",
            "#60EDF1F1" => "#70303437",
            "#FAFCF8" => "#181818",
            "#FFFFFF" or "#FDFEFC" or "#FDFFFA" => "#222222",
            "#FCFDFB" or "#F4F9ED" => "#2B2B2B",
            "#161B17" => "#F2F2F2",
            "#18251B" => "#FAFBF7",
            "#767D77" or "#81897B" or "#808080" => "#B8B8B8",
            "#427B0E" => "#AEE56A",
            "#EAF9CA" or "#ECFBCB" or "#ECFBCD" or "#E6FAB8" or "#F0F8E7" => "#35482A",
            "#D7F2AB" or "#A9D26E" => "#678849",
            "#8A3C16" => "#FFB98C",
            "#EFF3EA" => "#2B2B2B",
            "#EDF1F1" or "#D4DADB" => "#333333",
            "#DCE2D8" or "#DDE5D7" or "#E0E5DC" or "#E0E5DD" or "#E2E8E8" or "#E7EBE3" or "#EDF0E9" or "#BBCBC9" or "#DCE6E5" => "#454545",
            _ => light
        };
        return (Color)ColorConverter.ConvertFromString(_dark ? dark : light);
    }
    public static void Apply(bool dark)
    {
        _dark = dark;
        foreach (var entry in Colors) entry.Value.Set(ColorFor(entry.Key));
        var resources = Application.Current.Resources;
        foreach (var key in new List<object>(System.Linq.Enumerable.Cast<object>(resources.Keys)))
            if (key is string name && name.StartsWith("Theme", StringComparison.Ordinal) && name.Length == 11) resources[key] = Get("#" + name[5..]);
        resources[SystemColors.WindowBrushKey] = Get("#FFFFFF");
        resources[SystemColors.WindowTextBrushKey] = Get("#161B17");
        resources[SystemColors.ControlBrushKey] = Get("#FCFDFB");
        resources[SystemColors.ControlTextBrushKey] = Get("#161B17");
        resources[SystemColors.HighlightBrushKey] = Get("#EAF9CA");
        resources[SystemColors.HighlightTextBrushKey] = Get("#161B17");
        resources["Ink"] = Get("#161B17"); resources["Muted"] = Get("#767D77"); resources["Accent"] = Get("#427B0E");
    }
}
