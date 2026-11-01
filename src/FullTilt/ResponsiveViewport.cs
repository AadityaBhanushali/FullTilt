using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FullTilt;

// Reflow at native size first. Low effective resolutions retain the complete layout,
// with a uniform density adjustment rather than hiding targets or clipping cards.
public sealed class ResponsiveViewport : Decorator
{
    internal double Density { get; private set; } = 1;
    protected override Size MeasureOverride(Size available)
    {
        if (Child is null) return new Size();
        var width = double.IsFinite(available.Width) ? Math.Max(1, available.Width) : 1280;
        var height = double.IsFinite(available.Height) ? Math.Max(1, available.Height) : 720;
        Density = Math.Min(1, Math.Min(width / 1280, height / 720));
        Child.RenderTransform = new ScaleTransform(Density, Density);
        Child.Measure(new Size(width / Density, height / Density));
        return new Size(width, height);
    }
    protected override Size ArrangeOverride(Size size)
    {
        Child?.Arrange(new Rect(0, 0, size.Width / Density, size.Height / Density));
        return size;
    }
}
