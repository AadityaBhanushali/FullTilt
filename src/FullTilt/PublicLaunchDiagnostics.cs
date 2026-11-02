using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FullTilt.Core;

namespace FullTilt;

internal static class PublicLaunchDiagnostics
{
    internal static string Verify(string output)
    {
        var expected = typeof(PublicLaunchDiagnostics).Assembly.GetName().Version!.ToString(3);
        if (AppPreferencesViewModel.ReleaseVersion != expected || AppPreferencesViewModel.BuildVersion != expected) throw new InvalidOperationException("Displayed and assembly versions differ.");
        var links = LaunchLinks.Current;
        if (links.GitHubOwner != "AadityaBhanushali" || links.GitHubRepository != "FullTilt" || links.Issues?.AbsoluteUri != "https://github.com/AadityaBhanushali/FullTilt/issues") throw new InvalidOperationException("Public GitHub links are incorrect.");
        ThemePalette.Apply(true);
        using var model = new SettingsViewModel(FactoryDefaults.Create(), new SettingsStore(Path.Combine(output, "public-screenshot-settings")), null, null);
        var window = new MainWindow(model, new AppPreferencesViewModel()) { AllowClose = true, ShowActivated = false };
        try
        {
            if (((TextBlock)window.FindName("HeaderVersion")).Text.Trim() != expected) throw new InvalidOperationException("Header still shows a development version.");
            var tabs = (TabControl)window.FindName("SettingsTabs"); var root = (FrameworkElement)window.Content;
            foreach (var (tab, name) in new[] { (1, "keybindings"), (0, "movement"), (2, "pie-menus"), (4, "settings") })
            {
                tabs.SelectedIndex = tab;
                root.Measure(new Size(1536, 864)); root.Arrange(new Rect(0, 0, 1536, 864)); root.UpdateLayout();
                root.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background); root.UpdateLayout();
                var bitmap = new RenderTargetBitmap(1536, 864, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
                using var stream = File.Create(Path.Combine(output, "public-" + name + ".png"));
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(stream);
            }
            if (window.IsVisible || model.Settings.PrecisionCursorSpeed != FactoryDefaults.Create().PrecisionCursorSpeed || model.Edits.HasDrafts) throw new InvalidOperationException("Screenshot capture showed a window or edited defaults.");
        }
        finally { window.Close(); ThemePalette.Apply(false); }
        return "PASS: assembly/header/About/Settings version source, configured public repository, actual dark application screenshots using bundled defaults and isolated settings. No input service, live settings, browser or network was used. Screenshots are offscreen UI renders, not hardware validation.";
    }
}
