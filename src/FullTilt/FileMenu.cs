using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FullTilt;

internal static class FileMenu
{
    internal static Button Create(MainWindow window)
    {
        var button = new Button { Content = "_File ⌄", Height = 30, MinHeight = 0, Padding = new Thickness(12, 3, 12, 3), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(100, 0, 0, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        AutomationProperties.SetName(button, "File");
        var rows = new StackPanel(); KeyboardNavigation.SetTabNavigation(rows, KeyboardNavigationMode.Cycle); KeyboardNavigation.SetDirectionalNavigation(rows, KeyboardNavigationMode.Cycle);
        var frame = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(6), MinWidth = 242, Child = rows };
        frame.SetResourceReference(Border.BackgroundProperty, "ThemeFFFFFF"); frame.SetResourceReference(Border.BorderBrushProperty, "ThemeE0E5DC");
        var popup = new Popup { PlacementTarget = button, Placement = PlacementMode.Bottom, VerticalOffset = 4, AllowsTransparency = true, StaysOpen = false, Child = frame, Focusable = true };
        void Dismiss(Action? action = null)
        {
            popup.IsOpen = false; button.Focus();
            if (action is not null) window.Dispatcher.BeginInvoke(action);
        }
        var rowsByCommand = new System.Collections.Generic.Dictionary<string, Button>();
        var tourMode = false;
        foreach (var (label, glyph, command) in new[] { ("Take the tour", "", "tour"), ("Check for Updates", "\uE895", "updates"), ("Report a Problem", "\uE90A", "issues"), ("About FullTilt", "\uE946", "about"), ("Exit FullTilt", "\uE8BB", "exit") })
        {
            if (command == "exit") rows.Children.Add(new Separator { Margin = new Thickness(6, 4, 6, 4) });
            var content = new StackPanel { Orientation = Orientation.Horizontal, Width = 202 };
            var icon = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14, Width = 27, VerticalAlignment = VerticalAlignment.Center };
            icon.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); content.Children.Add(icon); content.Children.Add(new TextBlock { Text = label });
            var row = new Button { Content = content, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 9, 10, 9), Margin = new Thickness(0, 2, 0, 2), BorderThickness = new Thickness(0) };
            row.SetResourceReference(Control.BackgroundProperty, "ThemeFCFDFB"); AutomationProperties.SetName(row, label);
            row.Click += (_, _) => Dismiss(() => window.InvokeFileAction(command)); rows.Children.Add(row); rowsByCommand[command] = row;
        }
        popup.Opened += (_, _) =>
        {
            if (UiMotion.Enabled)
            {
                var move = new TranslateTransform(); frame.RenderTransform = move;
                move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-4, 0, TimeSpan.FromMilliseconds(150)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
            if (!tourMode) rows.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        };
        void Open() { popup.PopupAnimation = UiMotion.Enabled ? PopupAnimation.Fade : PopupAnimation.None; popup.IsOpen = true; }
        button.Click += (_, _) => { if (popup.IsOpen) Dismiss(); else Open(); };
        // Tour hook: hold the menu open (it stays out of the dim) with the tour row outlined, then put everything back.
        window.ShowFileMenuForTour = open =>
        {
            tourMode = open; popup.StaysOpen = open;
            foreach (var (command, row) in rowsByCommand)
            {
                row.BorderThickness = new Thickness(command == "tour" && open ? 2 : 0);
                if (command == "tour" && open) row.BorderBrush = new SolidColorBrush(Color.FromRgb(200, 243, 91));
            }
            if (open) Open(); else popup.IsOpen = false;
        };
        window.FileMenuBounds = root =>
        {
            frame.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var origin = button.TransformToVisual(root).Transform(new Point(0, button.ActualHeight + 4));
            return new Rect(origin, frame.DesiredSize);
        };
        popup.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; Dismiss(); }
            else if (e.Key is Key.Down or Key.Up) { e.Handled = true; (Keyboard.FocusedElement as UIElement)?.MoveFocus(new TraversalRequest(e.Key == Key.Down ? FocusNavigationDirection.Next : FocusNavigationDirection.Previous)); }
        };
        window.PreviewKeyDown += (_, e) => { if (e.SystemKey == Key.F && Keyboard.Modifiers == ModifierKeys.Alt) { e.Handled = true; Open(); } };
        window.Deactivated += (_, _) => popup.IsOpen = false;
        window.Closed += (_, _) => popup.IsOpen = false;
        return button;
    }
}
