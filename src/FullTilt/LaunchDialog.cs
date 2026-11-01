using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FullTilt.Core;
using FullTilt.Windows;

namespace FullTilt;

internal sealed class LaunchDialog : Window
{
    internal StackPanel Body { get; } = new() { Margin = new Thickness(28, 12, 28, 28) };
    private bool _closing;
    internal LaunchDialog(string title)
    {
        Title = title; Width = 540; SizeToContent = SizeToContent.Height; MaxHeight = Math.Max(300, SystemParameters.WorkArea.Height - 48);
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Style = (Style)Application.Current.FindResource(typeof(Window)); Icon = Application.Current.MainWindow?.Icon;
        Content = new ScrollViewer { Content = Body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        ThemedChrome.Apply(this, false);
        Loaded += (_, _) =>
        {
            Animate(true);
            MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        Closing += CloseAnimated;
        SizeChanged += (_, _) =>
        {
            if (!IsVisible || Owner is null) return;
            var handle = new WindowInteropHelper(this).Handle; var ownerHandle = new WindowInteropHelper(Owner).Handle;
            var monitor = NativeMethods.MonitorFromWindow(ownerHandle, 2);
            var info = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
            if (!NativeMethods.GetMonitorInfo(monitor, ref info) || !NativeMethods.GetWindowRect(ownerHandle, out var ownerBounds) || !NativeMethods.GetWindowRect(handle, out var bounds)) return;
            var scale = Math.Max(96, NativeMethods.GetDpiForWindow(handle)) / 96d;
            MaxHeight = Math.Max(100, (info.Work.Bottom - info.Work.Top - 32) / scale);
            MaxWidth = Math.Max(100, (info.Work.Right - info.Work.Left - 32) / scale);
            var width = bounds.Right - bounds.Left; var height = bounds.Bottom - bounds.Top;
            var fitted = WindowPlacement.Fit(new WindowBounds(ownerBounds.Left + (ownerBounds.Right - ownerBounds.Left - width) / 2, ownerBounds.Top + (ownerBounds.Bottom - ownerBounds.Top - height) / 2, width, height),
                new WindowBounds(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top));
            if (fitted.X != bounds.Left || fitted.Y != bounds.Top) NativeMethods.SetWindowPos(handle, 0, fitted.X, fitted.Y, 0, 0, 0x0015); // NOSIZE | NOZORDER | NOACTIVATE
        };
    }
    private void Animate(bool opening, Action? complete = null)
    {
        if (!UiMotion.Enabled) { complete?.Invoke(); return; }
        var duration = TimeSpan.FromMilliseconds(opening ? 200 : 140);
        var move = Body.RenderTransform as TranslateTransform ?? new TranslateTransform(); Body.RenderTransform = move;
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(opening ? -5 : 0, opening ? 0 : -3, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        var fade = new DoubleAnimation(opening ? 0 : 1, opening ? 1 : 0, duration);
        if (complete is not null) fade.Completed += (_, _) => complete();
        BeginAnimation(OpacityProperty, fade);
    }
    private void CloseAnimated(object? sender, CancelEventArgs e)
    {
        if (_closing || !UiMotion.Enabled) return;
        e.Cancel = true; _closing = true; Animate(false, Close);
    }
    internal TextBlock Text(string text, double size = 13, bool muted = false)
    {
        var block = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14), LineHeight = size * 1.5 };
        block.SetResourceReference(TextBlock.ForegroundProperty, muted ? "Muted" : "Ink"); Body.Children.Add(block); return block;
    }
    internal Button Button(Panel parent, string label, Action action, bool primary = false)
    {
        var button = new Button { Content = label, Margin = new Thickness(8, 0, 0, 0), MinWidth = 90 };
        if (primary) { button.Background = new SolidColorBrush(Color.FromRgb(200, 243, 91)); button.Foreground = new SolidColorBrush(Color.FromRgb(24, 37, 27)); }
        AutomationProperties.SetName(button, label); button.Click += (_, _) => action(); parent.Children.Add(button); return button;
    }
    internal StackPanel Actions()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        Body.Children.Add(row); return row;
    }
    internal void Preference(AppPreferencesViewModel preferences)
    {
        var toggle = new CheckBox { Content = "Automatically check for updates on startup", IsChecked = preferences.AutomaticallyCheckForUpdates, Margin = new Thickness(0, 8, 0, 12) };
        AutomationProperties.SetName(toggle, (string)toggle.Content);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) }; error.SetResourceReference(TextBlock.ForegroundProperty, "Theme8A3C16");
        toggle.Click += (_, _) => { preferences.AutomaticallyCheckForUpdates = toggle.IsChecked == true; toggle.IsChecked = preferences.AutomaticallyCheckForUpdates; error.Text = preferences.Error ?? ""; };
        Body.Children.Add(toggle); Body.Children.Add(error);
    }
    internal void Versions(ReleaseInfo release)
    {
        Text("Installed version  " + AppPreferencesViewModel.BuildVersion, muted: true);
        var latest = Text("Latest version  " + release.Version.ToString(3), 18); latest.FontWeight = FontWeights.SemiBold; latest.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
    }
    internal static LaunchDialog About(Action<Uri> open)
    {
        var dialog = new LaunchDialog("About FullTilt");
        dialog.Body.Children.Add(new BrandLogo { Width = 206, Height = 56, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 16) });
        dialog.Text("FullTilt", 26).FontWeight = FontWeights.SemiBold;
        dialog.Text("Version " + AppPreferencesViewModel.BuildVersion + " · Windows x64", muted: true);
        dialog.Text("Your controller, your desktop. FullTilt makes Windows navigation more comfortable and flexible by turning your game controller into an intuitive mouse and keyboard alternative.", muted: true);
        dialog.Text("Created by Aditya Bhanushali", 18).FontWeight = FontWeights.SemiBold;
        dialog.Text("I'm a motion and interaction designer who enjoys creating digital experiences and experimenting with new ways of interacting with technology. I built FullTilt to make desktop navigation more comfortable and accessible, and decided to share it freely with everyone.", muted: true);
        var links = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-8, 4, 0, 12) }; dialog.Body.Children.Add(links);
        foreach (var (label, url) in new[] { ("LinkedIn ↗", LaunchLinks.Current.LinkedIn), ("Behance ↗", LaunchLinks.Current.Behance) })
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https") dialog.Button(links, label, () => open(uri));
        dialog.Button(dialog.Actions(), "Close", dialog.Close); return dialog;
    }
    internal static LaunchDialog Automatic(ReleaseInfo release, AppPreferencesViewModel preferences, Action<Uri> open)
    {
        var dialog = new LaunchDialog("New update available"); dialog.Text("New update available", 25).FontWeight = FontWeights.SemiBold;
        dialog.Text("A new version of FullTilt is ready on GitHub.", muted: true); dialog.Versions(release);
        dialog.Text("Download the portable build from the release page. Exit FullTilt before replacing its files, and keep your Profiles folder.", muted: true);
        dialog.Preference(preferences); var row = dialog.Actions(); dialog.Button(row, "Later", dialog.Close);
        dialog.Button(row, "View Release", () => { open(release.Url); dialog.Close(); }, true); return dialog;
    }
}

// Dims only FullTilt's client area; never creates a desktop-wide or activating overlay.
internal sealed class DialogDimmer(UIElement target) : Adorner(target)
{
    protected override void OnRender(DrawingContext drawing) => drawing.DrawRectangle(new SolidColorBrush(Color.FromArgb(100, 0, 0, 0)), null, new Rect(AdornedElement.RenderSize));
}
