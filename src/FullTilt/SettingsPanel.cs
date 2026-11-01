using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Data;

namespace FullTilt;

// Keep controls at their native font size. Compact windows trade explanatory
// paragraphs and generous spacing for tooltips, rather than scaling the page.
public sealed class SettingsPanel : Panel
{
    private readonly Dictionary<FrameworkElement, Thickness> _margins = new();
    private readonly Dictionary<Border, Thickness> _padding = new();
    private readonly Dictionary<Button, (Thickness Padding, double MinHeight)> _controlPadding = new();
    private readonly Dictionary<TextBlock, Visibility> _descriptions = new();
    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(nameof(IsCompact), typeof(bool), typeof(SettingsPanel), new PropertyMetadata(false));
    public bool IsCompact { get => (bool)GetValue(IsCompactProperty); private set => SetValue(IsCompactProperty, value); }
    private bool? _compact;
    private static IEnumerable<FrameworkElement> Elements(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement element) yield return element;
            foreach (var descendant in Elements(child)) yield return descendant;
        }
    }
    private void Density(bool compact)
    {
        if (_compact == compact) return;
        _compact = compact; IsCompact = compact;
        foreach (UIElement card in InternalChildren)
        foreach (var element in new[] { (FrameworkElement)card }.Concat(Elements(card)))
        {
            if (element.TemplatedParent is not null) continue;
            if (element is TextBlock hint && hint.Name == "PauseShortcutHelp")
            {
                hint.TextWrapping = compact ? TextWrapping.NoWrap : TextWrapping.Wrap;
                hint.TextTrimming = compact ? TextTrimming.CharacterEllipsis : TextTrimming.None;
            }
            _margins.TryAdd(element, element.Margin);
            var margin = _margins[element];
            element.Margin = compact ? new Thickness(margin.Left * .4, margin.Top * .25, margin.Right * .4, margin.Bottom * .25) : new Thickness(margin.Left, margin.Top * .75, margin.Right, margin.Bottom * .75);
            if (element is CheckBox chip && chip.Style == TryFindResource("ShortcutChip")) chip.Padding = compact ? new Thickness(8, 5, 8, 5) : new Thickness(16, 10, 16, 10);
            if (element is Button button)
            {
                _controlPadding.TryAdd(button, (button.Padding, button.MinHeight));
                button.MinHeight = compact ? 30 : _controlPadding[button].MinHeight;
                button.Padding = compact ? new Thickness(8, 4, 8, 4) : _controlPadding[button].Padding;
            }
            if (element is Border border && border.Padding.Left > 0)
            {
                _padding.TryAdd(border, border.Padding);
                var pad = _padding[border];
                border.Padding = compact ? new Thickness(Math.Min(pad.Left, 8), Math.Min(pad.Top, 6), Math.Min(pad.Right, 8), Math.Min(pad.Bottom, 6)) : new Thickness(Math.Min(pad.Left, 16), Math.Min(pad.Top, 16), Math.Min(pad.Right, 16), Math.Min(pad.Bottom, 16));
            }
            if (element is TextBlock text && (text.Name is "ThemeLabel" or "ActiveControllerLabel" || text.Style == TryFindResource("Help")) && (!BindingOperations.IsDataBound(text, TextBlock.TextProperty) || BindingOperations.GetBinding(text, TextBlock.TextProperty)?.Path.Path == "StartupDescription"))
            {
                _descriptions.TryAdd(text, text.Visibility);
                text.Visibility = compact ? Visibility.Collapsed : _descriptions[text];
                // Keep secondary explanations available on the associated card.
                if (compact && card is FrameworkElement owner) owner.ToolTip = string.Join("\n", Elements(card).OfType<TextBlock>().Where(t => t.Style == TryFindResource("Help") && !string.IsNullOrEmpty(t.Text)).Select(t => t.Text));
            }
        }
    }
    protected override Size MeasureOverride(Size size)
    {
        Density(size.Height < 850 || size.Width < 1200);
        double left = 0, right = 0;
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            InternalChildren[i].Measure(new Size(size.Width * (i < 3 ? .42 : .58), double.PositiveInfinity));
            if (i < 3) left += InternalChildren[i].DesiredSize.Height; else right += InternalChildren[i].DesiredSize.Height;
        }
        return new Size(size.Width, Math.Min(size.Height, Math.Max(left, right)));
    }
    protected override Size ArrangeOverride(Size size)
    {
        double leftHeight = 0, rightHeight = 0;
        for (var i = 0; i < InternalChildren.Count; i++)
            if (i < 3) leftHeight += InternalChildren[i].DesiredSize.Height; else rightHeight += InternalChildren[i].DesiredSize.Height;
        double left = 0, right = 0;
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            var height = child.DesiredSize.Height + Math.Max(0, size.Height - 20 - (i < 3 ? leftHeight : rightHeight)) / (i < 3 ? 3 : 2);
            if (i < 3) { child.Arrange(new Rect(0, left, size.Width * .42, height)); left += height; }
            else { child.Arrange(new Rect(size.Width * .42, right, size.Width * .58, height)); right += height; }
        }
        return size;
    }
}
