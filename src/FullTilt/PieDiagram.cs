using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Automation;
using System.Windows.Media.Animation;
using FullTilt.Core;

namespace FullTilt;

// Variable geometry shared by editor and overlay. Slot identity never follows placement or content.
public sealed class PieDiagram : Viewbox
{
    private readonly Canvas _canvas = new() { Width = 400, Height = 400 };
    public event Action<int>? SelectedSlot;
    public event Action<int, object>? Dropped;
    internal event Action<FrameworkElement, InputAction, IDataObject>? DragRequested;
    public PieMenu? Menu { get; private set; }
    private Point _dragStart;
    private readonly List<Geometry> _segments = new();
    private Path? _dropHint;
    private int _selected = -1;
    internal int DropHover { get; private set; } = -1;
    internal void SetDropHover(int slot)
    {
        if (slot == DropHover) return;
        DropHover = slot;
        if (_dropHint is not null) _canvas.Children.Remove(_dropHint);
        _dropHint = null;
        if (slot < 0 || slot >= _segments.Count) return;
        _dropHint = new Path { Data = _segments[slot], Fill = Brush("#60EDF1F1"), Stroke = Brush("#84BC3B"), StrokeThickness = 2.5, IsHitTestVisible = false };
        _canvas.Children.Add(_dropHint);
        UiMotion.Reveal(_dropHint, 0);
    }
    internal int HitSlot(Point point) => _segments.FindIndex(shape => shape.FillContains(point));
    public PieDiagram() { Child = _canvas; Stretch = Stretch.Uniform; }
    private static SolidColorBrush Brush(string hex) => ThemePalette.Get(hex);
    private static Point At(double degrees, double radius) => new(200 + Math.Sin(degrees * Math.PI / 180) * radius, 200 - Math.Cos(degrees * Math.PI / 180) * radius);
    public void Render(PieMenu? menu, int selected, bool mouseHeld = false, bool editable = false, string? opener = null, PieMenu? saved = null, bool showDraft = false, bool desktop = false)
    {
        var restoreSlotFocus = editable && IsKeyboardFocusWithin;
        Button? selectedButton = null;
        var previous = Menu; var previousSelected = _selected;
        var surfaces = _canvas.Children.OfType<Path>().Where(p => p.IsHitTestVisible).Select(p => p.Fill).ToArray();
        Menu = menu; _selected = selected; _canvas.Children.Clear(); _segments.Clear(); _dropHint = null; DropHover = -1;
        if (!desktop)
        {
            var halo = new Ellipse { Width = 388, Height = 388, Fill = Brush("#60EDF1F1"), Stroke = PieGlass.Edge(), StrokeThickness = .7 };
            Canvas.SetLeft(halo, 6); Canvas.SetTop(halo, 6); _canvas.Children.Add(halo);
        }
        var count = menu?.SegmentCount ?? 4;
        for (var i = 0; i < count; i++)
        {
            var angle = PieGeometry.Center(count, i); var half = PieGeometry.Width(count) / 2;
            var slot = i; var action = menu?.Slots[i] ?? InputAction.None;
            var recorded = menu?.RecordedSlots.GetValueOrDefault(i);
            var dirty = showDraft && editable && (saved is null || i >= saved.Slots.Length || saved.Slots[i] != action || !KeyboardChord.Same(recorded, saved.RecordedSlots.GetValueOrDefault(i)));
            var disabled = recorded is null ? ActionCatalog.PieDisabledReason(action, mouseHeld) : mouseHeld ? "Release held mouse input first." : null;
            var active = selected == i;
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                const double outer = 175, inner = 59, corner = 4;
                var start = angle - half + 1.1; var end = angle + half - 1.1;
                var outerCorner = corner / outer * 180 / Math.PI;
                var innerCorner = corner / inner * 180 / Math.PI;
                context.BeginFigure(At(start + innerCorner, inner), true, true);
                context.QuadraticBezierTo(At(start, inner), At(start, inner + corner), true, false);
                context.LineTo(At(start, outer - corner), true, false);
                context.QuadraticBezierTo(At(start, outer), At(start + outerCorner, outer), true, false);
                context.ArcTo(At(end - outerCorner, outer), new Size(outer, outer), 0, false, SweepDirection.Clockwise, true, false);
                context.QuadraticBezierTo(At(end, outer), At(end, outer - corner), true, false);
                context.LineTo(At(end, inner + corner), true, false);
                context.QuadraticBezierTo(At(end, inner), At(end - innerCorner, inner), true, false);
                context.ArcTo(At(start + innerCorner, inner), new Size(inner, inner), 0, false, SweepDirection.Counterclockwise, true, false);
            }
            geometry.Freeze(); _segments.Add(geometry);
            var path = new Path { Data = geometry, Fill = dirty ? EditChrome.Cherry : desktop ? PieGlass.DesktopSurface(active) : PieGlass.Surface(active), Opacity = disabled is null ? 1 : .65, Stroke = active ? Brush("#A4EB42") : PieGlass.Edge(), StrokeThickness = 1.15,
                Effect = desktop ? null : new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = active ? .18 : .12 } };
            _canvas.Children.Add(path);
            if (editable && UiMotion.Enabled && previous?.Id == menu?.Id && previous?.SegmentCount == count && previousSelected != selected && i < surfaces.Length)
            {
                var oldSurface = new Path { Data = geometry, Fill = surfaces[i], IsHitTestVisible = false };
                _canvas.Children.Add(oldSurface);
                oldSurface.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150)) { FillBehavior = FillBehavior.HoldEnd });
            }
            if (!desktop) _canvas.Children.Add(new Path { Data = geometry, Fill = PieGlass.Sheen(), IsHitTestVisible = false });
            var label = (recorded?.Label ?? (action switch { InputAction.None => "Empty", InputAction.MinimizeWindow => "Minimize", InputAction.MaximizeWindow => "Maximize", InputAction.RestoreWindow => "Restore", InputAction.CloseWindow => "Close window", InputAction.SnapLeft => "Snap left", InputAction.SnapRight => "Snap right", _ => ActionCatalog.Get(action).Label })) + (disabled is null ? "" : "\nDisabled");
            var ink = active && !dirty ? Brushes.Black : Brush("#161B17");
            var text = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, FontSize = count <= 4 ? 13 : 11.5, FontWeight = FontWeights.Medium, Foreground = ink, MaxHeight = 48 };
            var contents = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            contents.Children.Add(new UiIcon { Kind = recorded is null ? action.ToString() : "ToggleKeyboard", Width = 28, Height = 28, Ink = ink, Margin = new Thickness(0, 0, 0, 7), HorizontalAlignment = HorizontalAlignment.Center });
            contents.Children.Add(text);
            if (dirty)
            {
                var old = saved is not null && i < saved.Slots.Length ? saved.SlotLabel(i) : "Empty";
                contents.ToolTip = "Unsaved · Saved: " + old;
            }
            var button = new Button { Content = contents, Width = count <= 4 ? 108 : 82, Height = 92, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), ToolTip = disabled ?? label, Focusable = editable, IsHitTestVisible = editable };
            // Slot buttons must not inherit the app's opaque hover surface.
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            button.Template = new ControlTemplate(typeof(Button)) { VisualTree = presenter };
            var point = At(angle, 122); Canvas.SetLeft(button, point.X - button.Width / 2); Canvas.SetTop(button, point.Y - 46);
            AutomationProperties.SetName(button, "Segment " + (i + 1) + " · " + PieGeometry.Label(count, i) + " · " + label);
            _canvas.Children.Add(button);
            if (editable && previous?.Id == menu?.Id && previous?.SegmentCount == count && previous.Slots[i] != action) UiMotion.Reveal(contents, 3);
            if (active) selectedButton = button;
            if (editable)
            {
                var clickSlot = slot;
                button.PreviewMouseLeftButtonUp += (_, e) => { var p = e.GetPosition(_canvas); clickSlot = HitSlot(p); };
                button.Click += (_, _) => { if (clickSlot >= 0) SelectedSlot?.Invoke(clickSlot); clickSlot = slot; };
                path.MouseLeftButtonUp += (_, e) =>
                {
                    var p = e.GetPosition(_canvas); var hit = HitSlot(p);
                    if (hit >= 0) SelectedSlot?.Invoke(hit);
                };
                foreach (var element in new FrameworkElement[] { path, button })
                {
                    element.AllowDrop = true;
                    element.PreviewMouseLeftButtonDown += (_, e) => _dragStart = e.GetPosition(this);
                    element.PreviewMouseMove += (_, e) =>
                    {
                        var pointNow = e.GetPosition(this);
                        if (menu is not null && (menu.Slots[slot] != InputAction.None || menu.RecordedSlots.ContainsKey(slot)) && e.LeftButton == MouseButtonState.Pressed && (Math.Abs(pointNow.X - _dragStart.X) >= SystemParameters.MinimumHorizontalDragDistance || Math.Abs(pointNow.Y - _dragStart.Y) >= SystemParameters.MinimumVerticalDragDistance))
                        {
                            DragRequested?.Invoke(element, menu.Slots[slot], PieDragData.ForSlot(menu.Id, slot)); e.Handled = true;
                        }
                    };
                    element.DragOver += (_, e) => { var target = HitSlot(e.GetPosition(_canvas)); var valid = target >= 0 && ValidDrop(e.Data, menu); SetDropHover(valid ? target : -1); e.Effects = valid ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
                    element.DragLeave += (_, _) => SetDropHover(-1);
                    element.Drop += (_, e) =>
                    {
                        var p = e.GetPosition(_canvas);
                        SetDropHover(-1); TryDrop(HitSlot(p), e.Data);
                        e.Handled = true;
                    };
                }
            }
        }
        var disk = new Ellipse { Width = 108, Height = 108, Fill = desktop ? PieGlass.DesktopSurface() : PieGlass.Surface(), Stroke = PieGlass.Edge(), StrokeThickness = 1.2 };
        Canvas.SetLeft(disk, 146); Canvas.SetTop(disk, 146); _canvas.Children.Add(disk);
        var center = new TextBlock
        {
            Width = 86, MaxHeight = 74, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
            FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brush("#161B17"),
            Text = menu is null ? "Create a menu" : !editable ? menu.Name : opener is not null ? "Hold " + opener : "Choose a\nsegment",
            ToolTip = menu?.Name
        };
        center.Measure(new Size(86, 90));
        Canvas.SetLeft(center, 157); Canvas.SetTop(center, 200 - center.DesiredSize.Height / 2); _canvas.Children.Add(center);
        if (restoreSlotFocus) selectedButton?.Focus();
        if (editable && (previous?.Id != menu?.Id || previous?.SegmentCount != count)) UiMotion.Reveal(_canvas);
    }
    private static bool ValidDrop(IDataObject data, PieMenu? menu) => menu is not null &&
        (PieDragData.TryAction(data, out var action) && ActionCatalog.SupportsPie(action) || PieDragData.TrySlot(data, out var slot) && slot.Menu == menu.Id && slot.Slot >= 0 && slot.Slot < menu.SegmentCount);
    public bool TryDrop(int slot, IDataObject data)
    {
        if (slot < 0 || slot >= (Menu?.SegmentCount ?? 0) || !ValidDrop(data, Menu)) return false;
        Dropped?.Invoke(slot, PieDragData.TryAction(data, out var action) ? action : PieDragData.TrySlot(data, out var source) ? source : InputAction.None);
        return true;
    }
}
public sealed record PieSlotDrag(Guid Menu, int Slot);
