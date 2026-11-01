using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FullTilt.Core;

namespace FullTilt;

public sealed class PieDPadStrip : Viewbox
{
    private readonly DispatcherTimer _flash = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private PieMenu? _menu;
    private ControllerButtons _previous, _highlight;
    internal ControllerButtons Highlighted => _highlight;
    private static readonly ControllerButtons[] Directions = [ControllerButtons.DPadUp, ControllerButtons.DPadDown, ControllerButtons.DPadLeft, ControllerButtons.DPadRight];
    public const double HeightRatio = 480d / 440;
    public static bool HasHints(PieMenu menu) => menu.DPadActions.Any(a => a is not null);
    public PieDPadStrip()
    {
        Stretch = Stretch.Uniform;
        _flash.Tick += (_, _) => { _flash.Stop(); _highlight = 0; Draw(); };
        Unloaded += (_, _) => Stop();
    }
    public void Stop() { _flash.Stop(); _previous = _highlight = 0; }
    public void Render(PieMenu menu, ControllerButtons pressed)
    {
        _menu = menu;
        var fresh = pressed & ~_previous; _previous = pressed;
        if (fresh != 0) { _highlight = fresh; _flash.Stop(); _flash.Start(); }
        Draw();
    }
    private void Draw()
    {
        if (_menu is null) return;
        var row = new Grid { Width = 424, Height = 28 };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(42) });
        row.Children.Add(new TextBlock { Text = "D-pad", Foreground = ThemePalette.Get("#808080"), FontSize = 10, VerticalAlignment = VerticalAlignment.Center });
        for (var i = 0; i < 4; i++)
        {
            row.ColumnDefinitions.Add(new());
            var hint = new DockPanel { Margin = new Thickness(2, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
            var active = (_highlight & Directions[i]) != 0;
            var arrow = new Border { Width = 20, Height = 23, CornerRadius = new CornerRadius(4), Background = PieGlass.Surface(active), BorderBrush = PieGlass.Edge(), BorderThickness = new Thickness(.7), Margin = new Thickness(0, 0, 4, 0),
                Child = new TextBlock { Text = PieDPadEditor.Arrows[i], Foreground = active ? Brushes.Black : ThemePalette.Get("#161B17"), FontSize = 17, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            DockPanel.SetDock(arrow, Dock.Left); hint.Children.Add(arrow);
            hint.Children.Add(new TextBlock { Text = _menu.DPadActions[i] is null ? "—" : _menu.DPadLabel(i), FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Foreground = ThemePalette.Get("#161B17") });
            Grid.SetColumn(hint, i + 1); row.Children.Add(hint);
        }
        Child = new Border { Width = 440, Height = 36, Padding = new Thickness(8, 3, 8, 3), CornerRadius = new CornerRadius(18), Background = PieGlass.Surface(), BorderBrush = PieGlass.Edge(), BorderThickness = new Thickness(.8), Child = row };
    }
}
