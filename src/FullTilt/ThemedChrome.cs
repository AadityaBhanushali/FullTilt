using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Shell;

namespace FullTilt;

// WPF owns non-client hit testing and resize/maximize behavior; only presentation is custom.
internal static class ThemedChrome
{
    public static void Apply(Window window, bool resizable)
    {
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = 40, ResizeBorderThickness = new Thickness(resizable && window is not MainWindow ? 6 : 0),
            GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false
        });
        var content = (UIElement)window.Content; window.Content = null;
        var root = new DockPanel { Background = window.Background, LastChildFill = true };
        var bar = new Grid { Height = 40, Background = window.Background };
        var title = new TextBlock { Text = window.Title, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12, Foreground = (Brush)window.FindResource("Muted") };
        bar.Children.Add(title);
        if (window is MainWindow main)
        {
            bar.Children.Add(new BrandLogo { LettermarkOnly = true, Width = 16, Height = 16, Margin = new Thickness(12, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center });
            title.Margin = new Thickness(36, 0, 0, 0);
            var file = FileMenu.Create(main);
            WindowChrome.SetIsHitTestVisibleInChrome(file, true);
            bar.Children.Add(file);
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 3, 6, 3) };
        Button Add(string glyph, string label, Action click)
        {
            var button = new Button { Content = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12,
                Width = 40, MinHeight = 0, Padding = new Thickness(0), Margin = new Thickness(2, 0, 0, 0),
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), ToolTip = label };
            AutomationProperties.SetName(button, label);
            WindowChrome.SetIsHitTestVisibleInChrome(button, true);
            button.Click += (_, _) => click(); buttons.Children.Add(button); return button;
        }
        if (resizable)
        {
            Add("\uE921", "Minimize", () => SystemCommands.MinimizeWindow(window));
            var maximize = Add("\uE922", "Maximize", () => { if (window.WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(window); else SystemCommands.MaximizeWindow(window); });
            window.StateChanged += (_, _) =>
            {
                var restored = window.WindowState == WindowState.Maximized;
                maximize.Content = restored ? "\uE923" : "\uE922";
                maximize.ToolTip = restored ? "Restore" : "Maximize";
                AutomationProperties.SetName(maximize, (string)maximize.ToolTip);
            };
        }
        Add("\uE8BB", "Close", window.Close);
        bar.Children.Add(buttons); DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar); root.Children.Add(content);
        window.Content = root;
    }
}
