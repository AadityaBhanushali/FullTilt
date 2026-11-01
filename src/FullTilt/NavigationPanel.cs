using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FullTilt;

// Visual order only; keep tab identities and editor navigation logic unchanged.
public sealed class NavigationPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        double height = 24, width = 0;
        foreach (UIElement child in InternalChildren) { child.Measure(new Size(availableSize.Width, double.PositiveInfinity)); height += child.DesiredSize.Height; width = System.Math.Max(width, child.DesiredSize.Width); }
        return new Size(width, height);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        double top = 12;
        foreach (var child in InternalChildren.Cast<UIElement>().OrderBy(c => (c as FrameworkElement)?.Tag?.ToString()))
        {
            if ((child as FrameworkElement)?.Tag?.ToString() == "4")
                top = System.Math.Max(top, finalSize.Height - child.DesiredSize.Height - 12);
            child.Arrange(new Rect(0, top, finalSize.Width, child.DesiredSize.Height));
            top += child.DesiredSize.Height;
        }
        return finalSize;
    }
}
