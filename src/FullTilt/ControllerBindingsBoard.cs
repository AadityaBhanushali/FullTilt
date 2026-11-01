using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using FullTilt.Core;
using Control = FullTilt.Core.Control;

namespace FullTilt;

// Keybindings' aligned assignment columns and diagram share the existing editor targets.
internal sealed class ControllerBindingsBoard : UserControl
{
    private readonly SettingsViewModel _model;
    private readonly Action<Control> _select;
    private readonly Action<CursorStick> _selectJoystick;
    private readonly Canvas _canvas = new() { Width = 1100, Height = 800 };
    private readonly BitmapImage _art = new(new Uri("pack://application:,,,/Assets/controller-3.5.png"));
    private Control _selected = Control.LT;
    private CursorStick? _stick;
    private bool _saveFailed;
    private double Density => Math.Clamp(Math.Min(_canvas.Width / 1100, _canvas.Height / 800), .55, 1);
    private static Brush Color(string value) => ThemePalette.Get(value);

    public ControllerBindingsBoard(SettingsViewModel model, Action<Control> select, Action<CursorStick> selectJoystick)
    {
        _model = model; _select = select; _selectJoystick = selectJoystick;
        Content = _canvas;
        SizeChanged += (_, _) => { _canvas.Width = Math.Max(1, ActualWidth); _canvas.Height = Math.Max(1, ActualHeight); Draw(); };
        model.PropertyChanged += (_, e) => { if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(SettingsViewModel.Settings)) Draw(); };
        model.Edits.Changed += Draw;
        Draw();
    }
    public void Select(Control control) { _selected = control; _stick = null; Draw(); }
    public void SelectJoystick(CursorStick stick) { _stick = stick; Draw(); }
    internal void VerifyJoystickTargets()
    {
        _canvas.UpdateLayout();
        foreach (var stick in new[] { CursorStick.Left, CursorStick.Right })
        {
            var ring = _canvas.Children.OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Press " + stick + " joystick travel target");
            var center = new Point(Canvas.GetLeft(ring) + ring.Width / 2, Canvas.GetTop(ring) + ring.Height / 2);
            Button? Owner(Point point)
            {
                var hit = VisualTreeHelper.HitTest(_canvas, point)?.VisualHit;
                while (hit is not null && hit is not Button) hit = VisualTreeHelper.GetParent(hit);
                return hit as Button;
            }
            if (ring.Clip.FillContains(new Point(ring.Width / 2, ring.Height / 2)) || Owner(new Point(Canvas.GetLeft(ring) + 2, center.Y)) != ring ||
                Owner(center) is not { } click || AutomationProperties.GetName(click) != "Press " + (stick == CursorStick.Left ? "L3" : "R3") + " diagram target")
                throw new InvalidOperationException("Joystick ring and stick click targets overlap or are not selectable: " + stick + ", ring=" + (Owner(new Point(Canvas.GetLeft(ring) + 2, center.Y)) is { } ringHit ? AutomationProperties.GetName(ringHit) : "none") + ", center=" + (Owner(center) is { } centerHit ? AutomationProperties.GetName(centerHit) : "none") + ", diameter=" + ring.Width);
        }
    }

    private void Draw()
    {
        _canvas.Children.Clear();
        Add(Text("Controller bindings", 34, true), 0, 0);
        Add(Text("Click a button or an assignment to edit.", 19, muted: true), 0, 49);
        Add(new Image { Source = _art, Width = 510 * _canvas.Width / 1100, Height = 340 * _canvas.Height / 800, Stretch = Stretch.Uniform }, 320, 245);
        Section("LEFT SHOULDER", 0, 120, 300);
        Section("RIGHT SHOULDER", 850, 120, 250);
        Section("LEFT STICK", 0, 295, 300);
        Section("FACE BUTTONS", 850, 295, 250);
        Section("D-PAD", 0, 480, 300);
        Section("RIGHT STICK", 850, 600, 250);
        ButtonRow(Control.LT, 0, 154, 300, 286, 231);
        ButtonRow(Control.LB, 0, 210, 300, 286, 272);
        ButtonRow(Control.RT, 850, 154, 250, 723, 231);
        ButtonRow(Control.RB, 850, 210, 250, 723, 272);
        TravelRow(CursorStick.Left, 0, 329, 300, 280, 373);
        ButtonRow(Control.L3, 0, 396, 300, 280, 373);
        ButtonRow(Control.Y, 850, 329, 250, 714, 327);
        ButtonRow(Control.X, 850, 385, 250, 656, 387);
        ButtonRow(Control.B, 850, 441, 250, 768, 387);
        ButtonRow(Control.A, 850, 497, 250, 714, 442);
        ButtonRow(Control.DPadUp, 0, 514, 300, 392, 465);
        ButtonRow(Control.DPadLeft, 0, 576, 300, 352, 505);
        ButtonRow(Control.DPadDown, 0, 638, 300, 392, 545);
        ButtonRow(Control.DPadRight, 0, 700, 300, 432, 505);
        TravelRow(CursorStick.Right, 850, 634, 250, 610, 508);
        ButtonRow(Control.R3, 850, 701, 250, 610, 508);
        EditingBar();
    }
    private void EditingBar()
    {
        var panel = new StackPanel { Width = 510 * _canvas.Width / 1100 };
        var dirty = _model.Edits.Dirty(EditTab.Keybindings);
        if (!dirty) _saveFailed = false;
        var status = Text(_saveFailed ? "Could not save · Drafts retained" : dirty ? $"● {_model.Edits.Count(EditTab.Keybindings)} unsaved bindings" : "Keybindings only · Changes saved", 13);
        status.ToolTip = _saveFailed ? _model.Warning : null;
        if (dirty) status.Foreground = EditChrome.CherryInk;
        panel.Children.Add(status);
        var buttons = new Grid { Margin = new Thickness(0, 8, 0, 6) };
        for (var i = 0; i < 4; i++) buttons.ColumnDefinitions.Add(new() { Width = new GridLength(i == 3 ? 1.45 : 1, GridUnitType.Star) });
        var undo = EditChrome.Button("Undo", () => _model.Edits.Replay(EditTab.Keybindings, true));
        var redo = EditChrome.Button("Redo", () => _model.Edits.Replay(EditTab.Keybindings, false));
        var discard = EditChrome.Button("Discard", () => _model.Edits.Discard(EditTab.Keybindings));
        var save = EditChrome.Button("Save changes", () => { _saveFailed = !_model.Edits.Save(EditTab.Keybindings); Draw(); }, true);
        undo.IsEnabled = _model.Edits.CanUndo(EditTab.Keybindings); redo.IsEnabled = _model.Edits.CanRedo(EditTab.Keybindings);
        discard.IsEnabled = save.IsEnabled = dirty;
        var index = 0;
        foreach (var button in new[] { undo, redo, discard, save })
        {
            button.Padding = new Thickness(3, 5, 3, 5); button.MinHeight = 0; button.FontSize = Math.Max(10, 13 * Density);
            Grid.SetColumn(button, index++); buttons.Children.Add(button);
        }
        panel.Children.Add(buttons); panel.Children.Add(Text("Changes take effect when you save.", 12, muted: true));
        Add(panel, 320, 630);
    }
    private TextBlock Text(string value, double size, bool bold = false, bool muted = false)
    {
        var text = new TextBlock { Text = value, FontSize = Math.Max(size <= 19 ? 11 : 16, size * Density), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty, muted ? "Muted" : "Ink");
        return text;
    }
    private void Section(string title, double x, double y, double width)
    {
        Add(new Line { X1 = x * _canvas.Width / 1100, X2 = (x + width) * _canvas.Width / 1100, Y1 = (y - 17) * _canvas.Height / 800, Y2 = (y - 17) * _canvas.Height / 800, Stroke = Color("#E7EBE3"), StrokeThickness = 1 }, 0, 0);
        Add(Text(title, 16, true, true), x + 5, y);
    }
    private void ButtonRow(Control control, double x, double y, double width, double diagramX, double diagramY)
    {
        var row = _model.Edits.BindingRows().Single(r => r.Control == control);
        var dirty = _model.Edits.BindingDirty(control);
        var assignment = row.Selection is KeyboardChord recorded ? recorded.Label : row.Options.Cast<BindingChoice>().FirstOrDefault(c => Equals(c.Value, row.Selection))?.Label ?? "None";
        string? direction = control >= Control.DPadUp ? control.ToString()[4..] : null;
        if (direction is not null && !_model.IsDPadActionMode) assignment = "Cursor " + direction.ToLowerInvariant();
        var active = _stick is null && _selected == control;
        Action activate = () => _select(control);
        Target(diagramX, diagramY, x < 500 ? x + width : x, y + 27, active, false, activate, "Press " + control + " diagram target", dirty);
        Row(control.ToString().StartsWith("DPad", StringComparison.Ordinal) ? "+" : control.ToString(), direction ?? assignment, direction is null ? null : assignment,
            x, y, width, active, activate, "Select " + control + " keybinding", control, dirty, EditorSession.BindingLabel(_model.Settings, control));
    }
    private void TravelRow(CursorStick stick, double x, double y, double width, double diagramX, double diagramY)
    {
        var active = _stick == stick;
        Action activate = () => _selectJoystick(stick);
        Target(diagramX, diagramY, x < 500 ? x + width : x, y + 28, active, true, activate, "Press " + stick + " joystick travel target", _model.Edits.RoleDirty);
        Row("◎", "Move", _model.Edits.View(EditTab.Keybindings).CursorStick == stick ? "Cursor movement" : "Scrolling", x, y, width, active, activate, "Select " + stick + " joystick travel", dirty: _model.Edits.RoleDirty, saved: _model.CursorStick == stick ? "Cursor movement" : "Scrolling");
    }
    private void Row(string badge, string title, string? subtitle, double x, double y, double width, bool active, Action activate, string name, Control? control = null, bool dirty = false, string? saved = null)
    {
        width *= _canvas.Width / 1100;
        var grid = new Grid { Width = Math.Max(1, width - 12) };
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(54 * Density) });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(20 * Density) });
        var key = Text(badge, badge is "+" or "◎" ? 30 : 19, true); key.HorizontalAlignment = HorizontalAlignment.Center;
        if (control is Control.A or Control.B or Control.X or Control.Y)
            key.Foreground = new SolidColorBrush(control switch { Control.A => Colors.Green, Control.B => Colors.Red, Control.X => Colors.DodgerBlue, _ => Colors.Goldenrod });
        var pill = new Border { Width = 44 * Density, Height = 44 * Density, CornerRadius = new CornerRadius((badge.Length == 1 ? 22 : 8) * Density), BorderThickness = new Thickness(1), Child = key, VerticalAlignment = VerticalAlignment.Center };
        pill.SetResourceReference(Border.BackgroundProperty, "ThemeFFFFFF"); pill.SetResourceReference(Border.BorderBrushProperty, "ThemeDCE2D8");
        grid.Children.Add(pill);
        var lines = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8 * Density, 0, 4 * Density, 0) };
        var titleText = Text(title, 18, subtitle is not null);
        titleText.TextWrapping = TextWrapping.NoWrap; titleText.TextTrimming = TextTrimming.CharacterEllipsis;
        lines.Children.Add(titleText);
        if (subtitle is not null) lines.Children.Add(Text(subtitle, 16, muted: true));
        Grid.SetColumn(lines, 1); grid.Children.Add(lines);
        var arrow = Text("›", 25, muted: true); Grid.SetColumn(arrow, 2); grid.Children.Add(arrow);
        var button = new Button { Content = grid, Width = width, Height = Math.Min((subtitle is null ? 54 : 60) * _canvas.Height / 800, 68), MinHeight = 0, Padding = new Thickness(3, 1, 3, 1), BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch, Background = dirty ? EditChrome.Cherry : active ? Color("#ECFBCB") : Brushes.Transparent, ToolTip = title + (subtitle is null ? "" : " · " + subtitle) + (dirty ? " · Unsaved · Saved: " + saved : "") };
        AutomationProperties.SetName(button, name); button.Click += (_, _) => activate(); Add(button, x, y);
    }
    private void Target(double oldX, double oldY, double rowX, double rowY, bool active, bool travel, Action activate, string name, bool dirty = false)
    {
        var sx = _canvas.Width / 1100; var sy = _canvas.Height / 800;
        var artScale = Math.Min(sx, sy);
        var x = 320 * sx + 510 * (sx - artScale) / 2 + (oldX - 100) * .6375 * artScale;
        var y = 245 * sy + 340 * (sy - artScale) / 2 + (oldY - 210) * .6375 * artScale;
        if (active) Add(new Line { X1 = x, Y1 = y, X2 = rowX * sx, Y2 = rowY * sy, Stroke = Color("#A4EB42"), StrokeThickness = 2 }, 0, 0);
        if (travel)
        {
            var size = 96 * .6375 * artScale;
            var inset = Math.Min(8, size / 4);
            var ring = new FrameworkElementFactory(typeof(Ellipse));
            ring.SetValue(Shape.StrokeProperty, dirty ? EditChrome.CherryInk : active ? Color("#A4EB42") : Brushes.White);
            ring.SetValue(Shape.StrokeThicknessProperty, active ? 3d : 2d);
            ring.SetValue(Shape.FillProperty, Brushes.Transparent);
            var targetRing = new Button { Width = size, Height = size, Padding = new Thickness(0), ToolTip = name, Focusable = false,
                Template = new ControlTemplate(typeof(Button)) { VisualTree = ring },
                Clip = new CombinedGeometry(GeometryCombineMode.Exclude, new EllipseGeometry(new Rect(0, 0, size, size)), new EllipseGeometry(new Rect(inset, inset, size - inset * 2, size - inset * 2))) };
            AutomationProperties.SetName(targetRing, name); targetRing.Click += (_, _) => activate(); AddAbsolute(targetRing, x - size / 2, y - size / 2);
            return; // The center remains the separate L3/R3 target.
        }
        var diameter = active || dirty ? 38 : 12;
        AddAbsolute(new Ellipse { Width = diameter, Height = diameter, Stroke = dirty ? EditChrome.CherryInk : active ? Color("#A4EB42") : Brushes.White, StrokeThickness = active || dirty ? 3 : 2, Fill = active || dirty ? Brushes.Transparent : Color("#DCE2D8"), IsHitTestVisible = false }, x - diameter / 2, y - diameter / 2);
        var target = new Button { Width = 28, Height = travel ? 20 : 28, Opacity = 0, ToolTip = name, Focusable = false };
        AutomationProperties.SetName(target, name); target.Click += (_, _) => activate(); AddAbsolute(target, x - 14, y - (travel ? 10 : 14));
    }
    private void AddAbsolute(UIElement element, double x, double y) { Canvas.SetLeft(element, x); Canvas.SetTop(element, y); _canvas.Children.Add(element); }
    private void Add(UIElement element, double x, double y) { Canvas.SetLeft(element, x * _canvas.Width / 1100); Canvas.SetTop(element, y * _canvas.Height / 800); _canvas.Children.Add(element); }
}
