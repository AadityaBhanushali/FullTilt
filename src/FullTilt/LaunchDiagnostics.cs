using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FullTilt.Core;

namespace FullTilt;

internal static class LaunchDiagnostics
{
    internal static string Verify(string output)
    {
        var directory = Path.Combine(output, "launch-isolated");
        var preferences = new AppPreferencesViewModel(new AppPreferencesStore(directory));
        preferences.CompleteOnboarding(); // These checks cover dialogs, not the first-run tour.
        using var model = new SettingsViewModel(new(), new SettingsStore(directory), null, null);
        var owner = new MainWindow(model, preferences) { AllowClose = true };
        using var features = new LaunchFeatures(owner, preferences, _ => { }); // Never Start: no timers or network.
        var release = new ReleaseInfo("v1.1.0", new Version(1, 1, 0, 0), "A sample stable release\n\n• Controller improvements\n• Profile compatibility retained", new Uri("https://github.com/AadityaBhanushali/FullTilt/releases/tag/v1.1.0"));
        void Render(LaunchDialog dialog, string name)
        {
            try
            {
                var root = (FrameworkElement)dialog.Content;
                root.Measure(new Size(540, 950)); var height = Math.Ceiling(root.DesiredSize.Height);
                root.Arrange(new Rect(0, 0, 540, height)); root.UpdateLayout();
                if (height > 950 || height < 100) throw new InvalidOperationException("Unexpected launch dialog bounds.");
                var image = new RenderTargetBitmap(540, (int)height, 96, 96, PixelFormats.Pbgra32); image.Render(root);
                using var stream = File.Create(Path.Combine(output, name + ".png")); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); encoder.Save(stream);
                if (dialog.IsVisible) throw new InvalidOperationException("Launch diagnostic must stay offscreen.");
            }
            finally { dialog.Close(); }
        }
        foreach (var dark in new[] { true, false })
        {
            ThemePalette.Apply(dark); var suffix = dark ? "-dark" : "-light";
            Render(LaunchDialog.About(_ => throw new InvalidOperationException("No browser in diagnostics")), "launch-about" + suffix);
            Render(LaunchDialog.Automatic(release, preferences, _ => throw new InvalidOperationException("No browser in diagnostics")), "launch-automatic" + suffix);
            var checking = new LaunchDialog("Check for Updates"); LaunchFeatures.RenderChecking(checking); Render(checking, "launch-checking" + suffix);
            foreach (var state in new[] { UpdateState.Available, UpdateState.Current, UpdateState.Failed, UpdateState.NoRelease, UpdateState.NotConfigured })
            {
                var dialog = new LaunchDialog("Check for Updates"); var retry = false;
                features.RenderResult(dialog, new(state, state == UpdateState.Current ? "You're up to date! You're running the latest version of FullTilt." : "Could not complete this check. Please try again.", state is UpdateState.Available or UpdateState.Current ? release : null), () => retry = true);
                var toggle = dialog.Body.Children.OfType<CheckBox>().Single(); toggle.IsChecked = false; toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                if (preferences.AutomaticallyCheckForUpdates) throw new InvalidOperationException("Update preference did not persist.");
                toggle.IsChecked = true; toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                if (!new AppPreferencesStore(directory).Load().AutomaticallyCheckForUpdates) throw new InvalidOperationException("Update preference failed restart.");
                if (state == UpdateState.Failed)
                {
                    var row = dialog.Body.Children.OfType<StackPanel>().Last(); row.Children.OfType<Button>().Single(b => Equals(b.Content, "Retry")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    if (!retry) throw new InvalidOperationException("Retry action was not invoked.");
                }
                Render(dialog, "launch-" + state + suffix);
            }
        }
        if (!preferences.AcknowledgeRelease(release.Tag) || new AppPreferencesStore(directory).Load().AcknowledgedRelease != release.Tag) throw new InvalidOperationException("Dismissed version did not persist.");
        owner.Close(); ThemePalette.Apply(false);
        return "PASS: offscreen dark/light About, automatic notification, checking, available/current/error/no-release/unconfigured states; retry handler, automatic-check toggle and acknowledged-version persistence. No network, browser, tray, controller service or live settings used. Visible focus, animated transitions and physical controller interaction require user validation.";
    }
}
