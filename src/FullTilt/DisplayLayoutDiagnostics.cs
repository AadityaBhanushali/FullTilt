using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FullTilt.Core;

namespace FullTilt;

internal static class DisplayLayoutDiagnostics
{
    internal static string Verify(string output)
    {
        using var model = new SettingsViewModel(PiePresets.Initialize(new(), bind: true), new SettingsStore(Path.Combine(output, "display-layout-isolated")), null, null);
        var window = new MainWindow(model) { AllowClose = true };
        var root = (FrameworkElement)window.Content; var tabs = (TabControl)window.FindName("SettingsTabs");
        var editor = (KeybindingsEditor)((ContentControl)window.FindName("KeybindingsHost")).Content;
        var cases = 0;
        foreach (var screen in new[] { (1280, 720), (1366, 768), (1920, 1080), (2560, 1440), (3440, 1440), (3840, 2160) })
        foreach (var dpi in new[] { 1d, 1.25, 1.5, 2 })
        foreach (var maximized in new[] { false, true })
        {
            var work = new WindowBounds(0, 0, screen.Item1, screen.Item2 - 48);
            var size = maximized ? (work.Width, work.Height) : WindowModes.WindowedSize(work, dpi);
            var width = size.Item1 / dpi; var height = size.Item2 / dpi;
            if (!maximized && size.Item1 * 9 != size.Item2 * 16) throw new InvalidOperationException("Normal mode is not 16:9.");
            foreach (var tab in new[] { 0, 1, 2, 4 })
            {
                tabs.SelectedIndex = tab; root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
                var label = $"{screen.Item1}x{screen.Item2}-dpi{dpi}-{(maximized ? "max" : "window")}-tab{tab}";
                if (tab == 1)
                {
                    var board = (ContentControl)editor.FindName("ControllerBoardHost"); var canvas = (Canvas)((ControllerBindingsBoard)board.Content).Content;
                    if (board.Visibility != Visibility.Visible || ((ComboBox)editor.FindName("ControlPicker")).Visibility != Visibility.Collapsed) throw new InvalidOperationException("Controller disappeared: " + label);
                    var rows = Descendants<Button>(board).Where(b => System.Windows.Automation.AutomationProperties.GetName(b).StartsWith("Select ")).ToArray();
                    if (rows.Length != 16 || canvas.Children.OfType<Image>().Single().ActualWidth < 100) throw new InvalidOperationException("Full controller UI is missing: " + label);
                    foreach (var row in rows) if (Canvas.GetTop(row) < 0 || Canvas.GetTop(row) + row.ActualHeight > canvas.ActualHeight + 1) throw new InvalidOperationException("Assignment outside board: " + label);
                    foreach (var group in rows.GroupBy(Canvas.GetLeft))
                    {
                        var ordered = group.OrderBy(Canvas.GetTop).ToArray(); for (var i = 1; i < ordered.Length; i++) if (Canvas.GetTop(ordered[i - 1]) + ordered[i - 1].ActualHeight > Canvas.GetTop(ordered[i]) + 1) throw new InvalidOperationException("Assignment rows overlap: " + label);
                    }
                    ((ControllerBindingsBoard)board.Content).VerifyJoystickTargets();
                    var actions = (ListBox)editor.FindName("Actions"); if (actions.ActualHeight < 70 || actions.ActualWidth < 150) throw new InvalidOperationException("Actions viewport is unusable: " + label);
                    foreach (var target in new[] { FullTilt.Core.Control.A, FullTilt.Core.Control.L3, FullTilt.Core.Control.R3 }) editor.SelectControl(target);
                    editor.SelectJoystick(CursorStick.Left); editor.SelectJoystick(CursorStick.Right); editor.SelectControl(FullTilt.Core.Control.LT);
                }
                if (tab == 4)
                {
                    var page = (FrameworkElement)window.FindName("SettingsFitViewport");
                    foreach (var name in new[] { "GeneralSettings", "AppearanceSettings", "ControllerCard", "PauseShortcutCard", "SharedSettingsFooter" })
                    {
                        var card = (FrameworkElement)window.FindName(name); var bounds = card.TransformToAncestor(page).TransformBounds(new Rect(card.RenderSize));
                        if (bounds.Right > page.ActualWidth + 1 || bounds.Bottom > page.ActualHeight + 1) throw new InvalidOperationException("Settings card is clipped: " + label + " " + name);
                    }
                }
                if (tab == 2)
                {
                    var pie = (PieEditor)((ContentControl)window.FindName("PieEditorHost")).Content;
                    var panes = Descendants<ListBox>(pie).ToArray(); if (panes.Length != 2 || panes.Any(p => p.ActualWidth < 150 || p.ActualHeight < 70)) throw new InvalidOperationException("Pie pane is not usable: " + label);
                    if (pie.Diagram.ActualWidth < 100 || pie.Diagram.ActualHeight != pie.Diagram.ActualWidth) throw new InvalidOperationException("Pie preview is not usable: " + label);
                }
                if (screen == (1920, 1080) && dpi == 1.5 || screen == (1366, 768) && dpi == 1.25 && maximized && tab == 1)
                {
                    foreach (var dark in new[] { false, true })
                    {
                        ThemePalette.Apply(dark); root.UpdateLayout(); var bitmap = new RenderTargetBitmap(size.Item1, size.Item2, 96 * dpi, 96 * dpi, PixelFormats.Pbgra32); bitmap.Render(root);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(output, "display-" + label + (dark ? "-dark" : "-light") + ".png")); encoder.Save(file);
                    }
                }
                cases++;
            }
        }
        window.Close(); ThemePalette.Apply(false);
        return $"PASS: {cases} page layouts across 720p, 768p, 1080p, 1440p, ultrawide and 4K at 100/125/150/200% DPI, windowed 16:9 and maximized modes. Controller and all 16 targets remain visible/selectable; joystick ring/click hits separate; rows do not overlap; action/pie panes usable; all five Settings cards fit. Representative laptop/dark/light renders. Isolated settings/offscreen render only; native display changes, shell snapping/maximize and hardware validation remain manual.";
    }
    private static System.Collections.Generic.IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is T item) yield return item; foreach (var descendant in Descendants<T>(child)) yield return descendant; }
    }
}
