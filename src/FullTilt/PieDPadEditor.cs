using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using FullTilt.Core;

namespace FullTilt;

// Compact reference artwork fits independently of the desktop menu size.
public sealed class PieDPadEditor : Viewbox
{
    public event Action<int>? SelectedDirection;
    public event Action<int, object>? Dropped;
    private PieMenu? _menu;
    private readonly Button?[] _keys = new Button?[4];
    private readonly Brush?[] _backgrounds = new Brush?[4], _borders = new Brush?[4];
    internal int DropHover { get; private set; } = -1;
    public static readonly string[] Arrows = ["↑", "↓", "←", "→"];
    public static string Label(InputAction? action) => action is { } value ? ActionCatalog.Get(value).Label : "No override";
    public PieDPadEditor() { Stretch = Stretch.Uniform; Unloaded += (_, _) => SetDropHover(-1); }
    public void Render(PieMenu? menu, PieMenu? saved, int selected)
    {
        _menu = menu; SetDropHover(-1);
        var root = new Grid { Width = 320, Height = 380 };
        var heading = Text("D-pad · " + (menu?.Name ?? "Menu"), 17); heading.FontWeight = FontWeights.SemiBold;
        heading.VerticalAlignment = VerticalAlignment.Top; root.Children.Add(heading);
        var help = Text("Only active while this menu is open.\nClick or drop an action on a direction.", 12); help.Margin = new Thickness(0, 30, 0, 0); help.VerticalAlignment = VerticalAlignment.Top; root.Children.Add(help);
        var canvas = new Canvas { Width = 320, Height = 280, VerticalAlignment = VerticalAlignment.Bottom }; root.Children.Add(canvas);
        var positions = new[] { new Point(134, 42), new Point(134, 146), new Point(82, 94), new Point(186, 94) };
        for (var direction = 0; direction < 4; direction++)
        {
            var index = direction; var action = menu?.DPadActions[index]; var previous = saved?.DPadActions[index];
            var dirty = menu is not null && (action != previous || !KeyboardChord.Same(menu.RecordedDPad.GetValueOrDefault(index), saved?.RecordedDPad.GetValueOrDefault(index)));
            var caption = menu?.DPadLabel(index) ?? Label(action); var oldCaption = saved?.DPadLabel(index) ?? Label(previous);
            var key = new Button { Content = Arrows[index], Width = 50, Height = 50, FontSize = 25, Padding = new Thickness(0),
                Foreground = ThemePalette.Get("#161B17"), Background = dirty ? EditChrome.Cherry : selected == index ? ThemePalette.Get("#ECFBCB") : ThemePalette.Get("#EDF1F1"),
                BorderBrush = dirty ? EditChrome.CherryInk : selected == index ? ThemePalette.Get("#84BC3B") : ThemePalette.Get("#DCE6E5"), BorderThickness = new Thickness(1) };
            key.Click += (_, _) => SelectedDirection?.Invoke(index);
            _keys[index] = key; _backgrounds[index] = key.Background; _borders[index] = key.BorderBrush;
            key.ToolTip = dirty ? "Unsaved · Saved: " + oldCaption : caption;
            AutomationProperties.SetName(key, "D-pad " + PieMenu.DPadDirections[index] + " · " + caption);
            Canvas.SetLeft(key, positions[index].X); Canvas.SetTop(key, positions[index].Y); canvas.Children.Add(key);
            var label = Text(caption, 12);
            label.FontWeight = FontWeights.Medium; label.MaxHeight = 48;
            label.TextTrimming = TextTrimming.CharacterEllipsis; label.Foreground = dirty ? EditChrome.CherryInk : ThemePalette.Get("#161B17");
            var captionFrame = new Border
            {
                Child = label, MaxWidth = index < 2 ? 200 : 78,
                Padding = new Thickness(8, 5, 8, 5), CornerRadius = new CornerRadius(8),
                Background = dirty ? EditChrome.Cherry : Brushes.Transparent,
                BorderBrush = dirty ? EditChrome.CherryInk : Brushes.Transparent, BorderThickness = new Thickness(1),
                ToolTip = key.ToolTip
            };
            // Content-sized pills stay centered on their button even when a
            // shortcut wraps. Up/down use the same axis; left/right share a centerline.
            captionFrame.SizeChanged += (_, _) =>
            {
                Canvas.SetLeft(captionFrame, index < 2 ? positions[index].X + 25 - captionFrame.ActualWidth / 2
                    : index == 2 ? positions[index].X - 4 - captionFrame.ActualWidth : positions[index].X + 54);
                Canvas.SetTop(captionFrame, index == 0 ? positions[index].Y - 8 - captionFrame.ActualHeight
                    : index == 1 ? positions[index].Y + 58 : positions[index].Y + 25 - captionFrame.ActualHeight / 2);
            };
            canvas.Children.Add(captionFrame);
            foreach (var target in new FrameworkElement[] { key, captionFrame })
            {
                target.AllowDrop = menu is not null;
                target.DragOver += (_, e) =>
                {
                    var valid = CanDrop(e.Data);
                    SetDropHover(valid ? index : -1);
                    e.Effects = valid ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true;
                };
                target.DragLeave += (_, _) => SetDropHover(-1);
                target.Drop += (_, e) => { e.Effects = TryDrop(index, e.Data) ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
            }
        }
        var footer = Text("Menu closed? Uses Keybindings defaults.", 11); footer.VerticalAlignment = VerticalAlignment.Bottom; root.Children.Add(footer);
        Child = root;
    }
    internal void SetDropHover(int direction)
    {
        DropHover = direction is >= 0 and < 4 ? direction : -1;
        for (var i = 0; i < 4; i++)
            if (_keys[i] is { } key)
            {
                key.Background = i == DropHover ? ThemePalette.Get("#ECFBCB") : _backgrounds[i];
                key.BorderBrush = i == DropHover ? ThemePalette.Get("#84BC3B") : _borders[i];
            }
    }
    internal bool CanDrop(IDataObject data) => TryAction(data, out _);
    private bool TryAction(IDataObject data, out object action)
    {
        action = InputAction.None;
        if (_menu is null) return false;
        if (PieDragData.TryAction(data, out var standard)) { action = standard; return true; }
        if (PieDragData.TrySlot(data, out var slot) && slot.Menu == _menu.Id && slot.Slot >= 0 && slot.Slot < _menu.SegmentCount)
        {
            if (_menu.RecordedSlots.TryGetValue(slot.Slot, out var chord)) { action = chord; return true; }
            var value = _menu.Slots[slot.Slot]; action = value; return value != InputAction.None && ActionCatalog.SupportsPie(value);
        }
        return false;
    }
    internal bool TryDrop(int direction, IDataObject data)
    {
        SetDropHover(-1);
        if (direction is < 0 or > 3 || !TryAction(data, out var action)) return false;
        Dropped?.Invoke(direction, action); return true;
    }
    private static TextBlock Text(string text, double size) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Foreground = ThemePalette.Get("#161B17") };
}
