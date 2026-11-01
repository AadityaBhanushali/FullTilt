using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using FullTilt.Core;

namespace FullTilt;

// Lives in the editor's adorner layer. No popup HWND, focus, capture or input injection.
internal sealed class PieDragVisual : Adorner
{
    private readonly Canvas _canvas = new();
    internal Border Card { get; }
    internal Point Position { get; private set; }
    public PieDragVisual(UIElement surface, InputAction action, KeyboardChord? recorded = null) : base(surface)
    {
        IsHitTestVisible = false;
        var row = new Grid(); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new UiIcon { Kind = recorded is null ? action.ToString() : "ToggleKeyboard", Width = 25, Height = 25, Margin = new Thickness(0, 0, 10, 0), Ink = ThemePalette.Get("#161B17") });
        var text = new TextBlock { Text = recorded?.Label ?? ActionCatalog.Get(action).Label, FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Foreground = ThemePalette.Get("#161B17") }; Grid.SetColumn(text, 1); row.Children.Add(text);
        Card = new Border { Child = row, MaxWidth = 240, Padding = new Thickness(12, 10, 12, 10), CornerRadius = new CornerRadius(12), Background = ThemePalette.Get("#FFFFFF"), BorderBrush = ThemePalette.Get("#84BC3B"), BorderThickness = new Thickness(1.5), Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = .2 } };
        _canvas.Children.Add(Card); AddVisualChild(_canvas); AddLogicalChild(_canvas);
    }
    internal void Follow(Point point)
    {
        Position = new Point(point.X + 16, point.Y + 18); Canvas.SetLeft(Card, Position.X); Canvas.SetTop(Card, Position.Y);
    }
    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => _canvas;
    protected override Size MeasureOverride(Size constraint) { _canvas.Measure(constraint); return AdornedElement.RenderSize; }
    protected override Size ArrangeOverride(Size finalSize) { _canvas.Arrange(new Rect(finalSize)); return finalSize; }
    internal static void Run(FrameworkElement surface, FrameworkElement source, InputAction action, IDataObject data, PieDiagram diagram, PieDPadEditor? dpad = null)
    {
        var recorded = PieDragData.TrySlot(data, out var slot) ? diagram.Menu?.RecordedSlots.GetValueOrDefault(slot.Slot) : null;
        var layer = AdornerLayer.GetAdornerLayer(surface); var visual = new PieDragVisual(surface, action, recorded);
        void Follow()
        {
            // OLE drag feedback does not keep WPF's cached Mouse position current.
            var cursor = System.Windows.Forms.Cursor.Position;
            visual.Follow(surface.PointFromScreen(new Point(cursor.X, cursor.Y)));
        }
        GiveFeedbackEventHandler feedback = (_, _) => Follow();
        try
        {
            if (layer is not null) { layer.Add(visual); Follow(); UiMotion.Reveal(visual.Card, 2); }
            source.GiveFeedback += feedback;
            DragDrop.DoDragDrop(source, data, DragDropEffects.Move);
        }
        finally { source.GiveFeedback -= feedback; layer?.Remove(visual); diagram.SetDropHover(-1); dpad?.SetDropHover(-1); }
    }
}
