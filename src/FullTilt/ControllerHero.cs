using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Automation;
using FullTilt.Core;
using Control = FullTilt.Core.Control;

namespace FullTilt;

internal sealed class ControllerHero : UserControl
{
    private readonly BitmapImage _art = new(new Uri("pack://application:,,,/Assets/controller-3.5.png"));
    private readonly Canvas _canvas = new() { Width = 1000, Height = 900 };
    private readonly SettingsViewModel _model;
    private readonly Action<Control> _select;
    private readonly Action<CursorStick> _selectJoystick;
    private Control _selected = Control.A;
    private CursorStick? _selectedJoystick;
    private static Brush Brush(string hex) => ThemePalette.Get(hex);
    public ControllerHero(SettingsViewModel model, Action<Control> select, Action<CursorStick> selectJoystick)
    {
        _model = model; _select = select; _selectJoystick = selectJoystick;
        Content = new Viewbox { Child = _canvas, Stretch = Stretch.Uniform };
        model.PropertyChanged += (_, e) => { if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(SettingsViewModel.Settings)) Draw(); };
        Draw();
    }
    public void Select(Control control) { _selectedJoystick = null; _selected = control; Draw(); }
    public void SelectJoystick(CursorStick stick) { _selectedJoystick = stick; Draw(); }
    private void Draw()
    {
        _canvas.Children.Clear();
        for (int x = 0; x <= 1000; x += 32) Add(new Line { X1 = x, X2 = x, Y1 = 30, Y2 = 880, Stroke = Brush("#EFF3EA"), StrokeThickness = .7 }, 0, 0);
        for (int y = 30; y <= 880; y += 32) Add(new Line { X1 = 0, X2 = 1000, Y1 = y, Y2 = y, Stroke = Brush("#EFF3EA"), StrokeThickness = .7 }, 0, 0);
        Add(new Image { Source = _art, Width = 800, Height = 534, Stretch = Stretch.Uniform }, 100, 210);
        Callout(Control.LT, 286, 231, 10, 20);
        Callout(Control.LB, 286, 272, 210, 90);
        Callout(Control.RT, 723, 231, 790, 20);
        Callout(Control.RB, 723, 272, 580, 90);
        Callout(Control.Y, 714, 327, 810, 260);
        Callout(Control.B, 768, 387, 810, 390);
        Callout(Control.A, 714, 442, 810, 500);
        Callout(Control.X, 656, 387, 410, 190);
        JoystickCallout(CursorStick.Left, 280, 373, 8, 340);
        JoystickCallout(CursorStick.Right, 610, 508, 750, 660);
        Callout(Control.L3, 280, 373, 210, 650);
        Callout(Control.R3, 610, 508, 500, 650);
        Callout(Control.DPadUp, 392, 465, 10, 600);
        Callout(Control.DPadLeft, 352, 505, 210, 780);
        Callout(Control.DPadDown, 392, 545, 420, 780);
        Callout(Control.DPadRight, 432, 505, 630, 780);
        Add(new TextBlock { Text = "Select a button or joystick travel to edit", Foreground = Brush("#81897B"), FontSize = 16 }, 350, 875);
    }
    private void JoystickCallout(CursorStick stick, double x, double y, double labelX, double labelY)
    {
        var active = _selectedJoystick == stick;
        var label = stick == CursorStick.Left ? "Left joystick travel" : "Right joystick travel";
        Add(new Line { X1 = x, Y1 = y - 48, X2 = labelX + 85, Y2 = labelY + 26, Stroke = Brush("#B6E86B"), StrokeThickness = 2 }, 0, 0);
        Add(new Ellipse { Width = 96, Height = 96, Stroke = Brush(active ? "#A4EB42" : "#B6E86B"), StrokeThickness = active ? 6 : 2, Fill = Brushes.Transparent, IsHitTestVisible = false }, x - 48, y - 48);
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, FontSize = 17 });
        text.Children.Add(new TextBlock { Text = _model.CursorStick == stick ? "Cursor Movement" : "Scrolling", FontSize = 15, Margin = new Thickness(0, 3, 0, 0) });
        var button = new Button { Content = text, Width = 186, Padding = new Thickness(13, 11, 13, 11), Background = Brush(active ? "#E6FAB8" : "#FDFFFA"), ToolTip = "Edit " + label };
        AutomationProperties.SetName(button, "Select " + stick + " joystick travel");
        button.Click += (_, _) => _selectJoystick(stick); Add(button, labelX, labelY);
        var hotspot = new Button { Width = 40, Height = 30, Opacity = 0, ToolTip = "Edit " + label, Focusable = false };
        AutomationProperties.SetName(hotspot, "Press " + stick + " joystick travel target");
        hotspot.Click += (_, _) => _selectJoystick(stick); Add(hotspot, x - 20, y - 60);
    }
    private void Callout(Control control, double x, double y, double labelX, double labelY)
    {
        var active = _selectedJoystick is null && control == _selected;
        var row = _model.Bindings.Concat(_model.DPadBindings).FirstOrDefault(r => r.Control == control);
        var label = row?.Options.Cast<BindingChoice>().FirstOrDefault(c => Equals(c.Value, row.Selection))?.Label ?? "Unassigned";
        if (control >= Control.DPadUp && !_model.IsDPadActionMode)
            label = control switch { Control.DPadUp => "Cursor up", Control.DPadDown => "Cursor down", Control.DPadLeft => "Cursor left", _ => "Cursor right" };
        var smallTarget = control >= Control.DPadUp || control is Control.LB or Control.RB or Control.LT or Control.RT or Control.L3 or Control.R3;
        var diameter = active ? (smallTarget ? 32 : 64) : 14;
        Add(new Line { X1 = x, Y1 = y, X2 = labelX + 85, Y2 = labelY + 26, Stroke = Brush("#B6E86B"), StrokeThickness = 2 }, 0, 0);
        Add(new Ellipse { Width = diameter, Height = diameter, Stroke = active ? Brush("#A4EB42") : Brushes.White, Fill = active ? Brushes.Transparent : Brush("#B6E86B"), StrokeThickness = active ? 6 : 3, IsHitTestVisible = false }, x - diameter / 2d, y - diameter / 2d);
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = row?.Label ?? control.ToString(), TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, FontSize = 17 });
        text.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, FontSize = 15, Margin = new Thickness(0, 3, 0, 0) });
        var button = new Button { Content = text, Width = 186, Padding = new Thickness(13, 11, 13, 11), Background = Brush(active ? "#E6FAB8" : "#FDFFFA"), ToolTip = "Edit " + control };
        AutomationProperties.SetName(button, "Select " + control + " keybinding");
        button.Click += (_, _) => _select(control);
        Add(button, Math.Min(labelX, 810), labelY);
        var hitSize = smallTarget ? 34 : 50;
        var hotspot = new Button { Width = hitSize, Height = hitSize, Opacity = 0, ToolTip = "Edit " + control, Focusable = false };
        AutomationProperties.SetName(hotspot, "Press " + control + " diagram target");
        hotspot.Click += (_, _) => _select(control); Add(hotspot, x - hitSize / 2d, y - hitSize / 2d);
    }
    private void Add(UIElement element, double x, double y) { Canvas.SetLeft(element, x); Canvas.SetTop(element, y); _canvas.Children.Add(element); }
}
